import CoreBluetooth
import Foundation
import WatchCore
import WatchKit

/// A PC running the WatchPointer receiver, seen nearby.
struct FoundPC: Identifiable {
    let id: UUID
    let name: String
    let rssi: Int
    let peripheral: CBPeripheral
}

/// The Bluetooth side of the watch app. watchOS apps can only be a BLE *central*, so the watch scans for and connects
/// to the PC, which hosts the GATT server (same architecture as the original WatchPointer with a Mac).
///
/// Writes: aim and scroll go "without response" (fast lane: if the link is busy the next sample replaces this one);
/// clicks, drags, gestures, pause and info go "with response" (must not be dropped).
final class PCLink: NSObject, ObservableObject, CBCentralManagerDelegate, CBPeripheralDelegate {
    @Published private(set) var connected = false
    @Published private(set) var bluetoothReady = true
    @Published var paused = false
    /// PCs nearby you can pair with (only while nothing is paired).
    @Published private(set) var found: [FoundPC] = []
    /// The PC you paired with. The watch only connects to this one until you forget it.
    @Published private(set) var pairedName = UserDefaults.standard.string(forKey: Keys.pairedName)
    /// v2: what the PC reports about itself.
    @Published private(set) var pcProfile: String?
    @Published private(set) var pcPaused = false
    @Published private(set) var pcSupportsV2 = false

    private enum Keys {
        static let pairedID = "pairedPCID"
        static let pairedName = "pairedPCName"
    }

    private let serviceUUID = CBUUID(string: WatchPointerUUID.service)
    private let inputUUID = CBUUID(string: WatchPointerUUID.input)
    private let outputUUID = CBUUID(string: WatchPointerUUID.output)
    private let infoUUID = CBUUID(string: WatchPointerUUID.info)

    private var pairedID = UserDefaults.standard.string(forKey: Keys.pairedID).flatMap(UUID.init)
    private var central: CBCentralManager!
    private var pc: CBPeripheral?
    private var input: CBCharacteristic?
    private var output: CBCharacteristic?
    private var infoTimer: Timer?
    private var pendingScroll = ScrollAccumulator()

    override init() {
        super.init()
        central = CBCentralManager(delegate: self, queue: .main)
    }

    // MARK: Sending

    /// `at`: when it happened on the watch's clock (ProcessInfo.systemUptime, the same clock as motion timestamps).
    func send(_ kind: PacketKind, _ a: Int16 = 0, _ b: Int16 = 0, at time: TimeInterval? = nil) {
        guard let pc, let input else { return }
        let bytes = PacketEncoder.packet(kind, a, b, ms: time.map(PacketEncoder.watchMs))
        pc.writeValue(Data(bytes), for: input, type: kind == .aim ? .withoutResponse : .withResponse)
    }

    func sendGesture(_ gesture: WatchGesture, at time: TimeInterval = ProcessInfo.processInfo.systemUptime) {
        send(.gesture, gesture.rawValue, at: time)
    }

    /// Called for every motion sample while connected.
    func sendAim(leftDegrees: Double, upDegrees: Double, at timestamp: TimeInterval) {
        guard let pc, let input, pc.canSendWriteWithoutResponse else { return } // busy: the next sample replaces this one
        pc.writeValue(Data(PacketEncoder.aim(leftDegrees: leftDegrees, upDegrees: upDegrees, at: timestamp)), for: input, type: .withoutResponse)
    }

    /// Crown turn in PC "pixels". Fractions are kept so slow turns still scroll.
    func scroll(by pixels: Double) {
        pendingScroll.add(pixels)
        flushScroll()
    }

    /// Scroll goes on the fast lane too; if the link is busy the amount waits for the next sample (100× a second).
    func flushScroll() {
        guard let pc, let input, pc.canSendWriteWithoutResponse, abs(pendingScroll.pending) >= 1 else { return }
        let whole = pendingScroll.takeWhole()
        pc.writeValue(Data(PacketEncoder.packet(.scroll, whole)), for: input, type: .withoutResponse)
    }

    func togglePause() {
        paused.toggle()
        send(.pause, paused ? 1 : 0)
    }

    /// Tells the PC which watch this is. Sent on connect, then every minute for the battery level.
    private func sendInfo() {
        guard let pc, let input else { return }
        var system = utsname()
        uname(&system)
        let model = withUnsafeBytes(of: &system.machine) { String(decoding: $0.prefix { $0 != 0 }, as: UTF8.self) }
        let device = WKInterfaceDevice.current()
        device.isBatteryMonitoringEnabled = true
        let battery = device.batteryLevel < 0 ? -1 : Int((device.batteryLevel * 100).rounded())
        let charging = device.batteryState == .charging || device.batteryState == .full
        let version = Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "1.0"
        let packet = PacketEncoder.info(model: model, os: device.systemVersion, battery: battery, charging: charging, name: device.name, appVersion: version)
        pc.writeValue(Data(packet), for: input, type: .withResponse)
    }

    // MARK: Pairing

    func centralManagerDidUpdateState(_ central: CBCentralManager) {
        bluetoothReady = central.state == .poweredOn
        if central.state == .poweredOn { start() }
    }

    /// Paired: wait for that PC (the connect completes whenever it's in range). Not paired: list what's nearby.
    private func start() {
        guard central.state == .poweredOn, pc == nil else { return }
        found = []
        if let pairedID, let known = central.retrievePeripherals(withIdentifiers: [pairedID]).first {
            connect(to: known)
        }
        central.scanForPeripherals(withServices: [serviceUUID])
    }

    func centralManager(_ central: CBCentralManager, didDiscover peripheral: CBPeripheral,
                        advertisementData: [String: Any], rssi RSSI: NSNumber) {
        if let pairedID {
            if peripheral.identifier == pairedID { connect(to: peripheral) }
            return
        }
        // Windows doesn't put its name in the advertisement, so new PCs may show a generic name until connected.
        let name = advertisementData[CBAdvertisementDataLocalNameKey] as? String ?? peripheral.name ?? "Windows PC"
        if let index = found.firstIndex(where: { $0.id == peripheral.identifier }) {
            if found[index].name != name { found[index] = FoundPC(id: peripheral.identifier, name: name, rssi: RSSI.intValue, peripheral: peripheral) }
            return
        }
        found.append(FoundPC(id: peripheral.identifier, name: name, rssi: RSSI.intValue, peripheral: peripheral))
        found.sort { $0.rssi > $1.rssi } // closest first
    }

    func pair(_ pc: FoundPC) {
        pairedID = pc.id
        pairedName = pc.name
        UserDefaults.standard.set(pc.id.uuidString, forKey: Keys.pairedID)
        UserDefaults.standard.set(pc.name, forKey: Keys.pairedName)
        found = []
        connect(to: pc.peripheral)
    }

    /// Unpair, drop the connection and go back to listing nearby PCs.
    func forget() {
        pairedID = nil
        pairedName = nil
        UserDefaults.standard.removeObject(forKey: Keys.pairedID)
        UserDefaults.standard.removeObject(forKey: Keys.pairedName)
        if let pc { central.cancelPeripheralConnection(pc) }
        reset()
    }

    private func connect(to peripheral: CBPeripheral) {
        guard pc == nil else { return }
        central.stopScan() // scanning costs battery; stop as soon as we know which PC
        pc = peripheral
        peripheral.delegate = self
        central.connect(peripheral)
    }

    func centralManager(_ central: CBCentralManager, didConnect peripheral: CBPeripheral) {
        peripheral.discoverServices([serviceUUID])
    }

    func centralManager(_ central: CBCentralManager, didFailToConnect peripheral: CBPeripheral, error: Error?) {
        reset()
    }

    func centralManager(_ central: CBCentralManager, didDisconnectPeripheral peripheral: CBPeripheral, error: Error?) {
        reset()
    }

    func peripheral(_ peripheral: CBPeripheral, didDiscoverServices error: Error?) {
        guard let service = peripheral.services?.first(where: { $0.uuid == serviceUUID }) else { return }
        peripheral.discoverCharacteristics([inputUUID, outputUUID, infoUUID], for: service)
    }

    func peripheral(_ peripheral: CBPeripheral, didDiscoverCharacteristicsFor service: CBService, error: Error?) {
        input = service.characteristics?.first { $0.uuid == inputUUID }
        output = service.characteristics?.first { $0.uuid == outputUUID }
        if let output { peripheral.setNotifyValue(true, for: output) }
        if let info = service.characteristics?.first(where: { $0.uuid == infoUUID }) {
            pcSupportsV2 = true
            peripheral.readValue(for: info)
        }
        connected = input != nil
        sendInfo()
        infoTimer?.invalidate()
        infoTimer = Timer.scheduledTimer(withTimeInterval: 60, repeats: true) { [weak self] _ in self?.sendInfo() }
    }

    func peripheral(_ peripheral: CBPeripheral, didUpdateValueFor characteristic: CBCharacteristic, error: Error?) {
        guard error == nil, let value = characteristic.value else { return }
        if characteristic.uuid == infoUUID, let info = PCInfo.decode(value), !info.name.isEmpty {
            // Remember the PC's real name (the advertisement may not have carried it).
            pairedName = info.name
            UserDefaults.standard.set(info.name, forKey: Keys.pairedName)
            return
        }
        guard characteristic.uuid == outputUUID, let message = PCNotification.decode([UInt8](value)) else { return }
        switch message {
        case let .ping(sequence):
            guard let input else { return }
            let pong = Data(PCNotification.pong(sequence: sequence, at: ProcessInfo.processInfo.systemUptime))
            peripheral.writeValue(pong, for: input, type: peripheral.canSendWriteWithoutResponse ? .withoutResponse : .withResponse)
        case let .state(paused, _, profile):
            pcPaused = paused
            pcProfile = profile.isEmpty ? nil : profile
        }
    }

    func peripheralIsReady(toSendWriteWithoutResponse peripheral: CBPeripheral) {
        flushScroll()
    }

    private func reset() {
        pc = nil
        input = nil
        output = nil
        connected = false
        paused = false
        pcProfile = nil
        pcPaused = false
        pcSupportsV2 = false
        pendingScroll.reset()
        infoTimer?.invalidate()
        start()
    }
}
