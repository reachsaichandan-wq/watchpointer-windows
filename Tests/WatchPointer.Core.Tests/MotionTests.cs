using WatchPointer.Core.Motion;

namespace WatchPointer.Core.Tests;

public class OneEuroFilterTests
{
    [Fact]
    public void First_sample_passes_through()
    {
        var f = new OneEuroFilter();
        Assert.Equal(42, f.Filter(42, 0.01));
    }

    [Fact]
    public void Constant_input_stays_constant()
    {
        var f = new OneEuroFilter();
        for (var i = 0; i < 100; i++) Assert.Equal(5, f.Filter(5, 0.01), 9);
    }

    [Fact]
    public void Converges_to_a_step()
    {
        var f = new OneEuroFilter();
        f.Filter(0, 0.01);
        double y = 0;
        for (var i = 0; i < 300; i++) y = f.Filter(100, 0.01);
        Assert.InRange(y, 99, 100.0001);
    }

    [Fact]
    public void Reduces_jitter_when_still()
    {
        var f = new OneEuroFilter(minCutoff: 0.8, beta: 0.01);
        var random = new Random(1);
        double rawVar = 0, filteredVar = 0;
        var n = 2000;
        for (var i = 0; i < n; i++)
        {
            var x = (random.NextDouble() - 0.5) * 10;
            var y = f.Filter(x, 0.01);
            if (i < 100) continue;
            rawVar += x * x;
            filteredVar += y * y;
        }
        Assert.True(filteredVar < rawVar / 20, $"filtered variance {filteredVar / n:F3} vs raw {rawVar / n:F3}");
    }

    [Fact]
    public void Lags_less_on_fast_moves_with_higher_beta()
    {
        double Lag(double beta)
        {
            var f = new OneEuroFilter(0.8, beta);
            double lag = 0;
            for (var i = 0; i < 100; i++)
            {
                var x = i * 20.0; // 2000 px/s ramp
                lag = x - f.Filter(x, 0.01);
            }
            return lag;
        }
        Assert.True(Lag(0.05) < Lag(0.001) / 2);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Invalid_dt_passes_value_through(double dt)
    {
        var f = new OneEuroFilter();
        f.Filter(0, 0.01);
        Assert.Equal(50, f.Filter(50, dt));
    }

    [Fact]
    public void Reset_forgets_history()
    {
        var f = new OneEuroFilter();
        f.Filter(0, 0.01);
        f.Filter(10, 0.01);
        f.Reset();
        Assert.False(f.HasValue);
        Assert.Equal(1000, f.Filter(1000, 0.01));
    }
}

public class WatchClockTests
{
    [Fact]
    public void Unwraps_16_bit_milliseconds()
    {
        var c = new WatchClock();
        Assert.Null(c.Observe(65530, 100.0, fresh: true));
        var dt = c.Observe(4, 100.010, fresh: false); // wrapped: 65530 → 65536+4
        Assert.Equal(0.010, dt!.Value, 6);
        Assert.Equal(65.540, c.WatchTime, 6);
    }

    [Fact]
    public void Offset_tracks_the_quickest_sample()
    {
        var c = new WatchClock { OffsetDriftPerSecond = 0 };
        c.Observe(1000, 50.030, true);       // 30 ms late
        c.Observe(1010, 50.025, false);      // 15 ms late: quicker
        c.Observe(1020, 50.060, false);      // 40 ms late
        Assert.Equal(50.025 - 1.010, c.Offset!.Value, 6);
        Assert.Equal(0.025, c.ExcessDelay(50.060), 6);
    }

    [Fact]
    public void Offset_relaxes_slowly_so_clock_drift_cannot_freeze_it()
    {
        var c = new WatchClock { OffsetDriftPerSecond = 1e-3 };
        c.Observe(0, 10.000, true);
        var start = c.Offset!.Value;
        // 10 s later, every sample arrives 5 ms "later" (watch clock running slow) → minimum must move up.
        c.Observe(10_000, 20.005, false);
        Assert.True(c.Offset > start);
        Assert.True(c.Offset <= start + 0.005 + 1e-9);
    }

    [Fact]
    public void Converts_event_stamps_before_and_after_the_last_sample()
    {
        var c = new WatchClock { OffsetDriftPerSecond = 0 };
        c.Observe(2000, 100.0, true);
        Assert.Equal(100.0 - 0.050, c.ToPcTime(1950)!.Value, 6);
        Assert.Equal(100.0 + 0.020, c.ToPcTime(2020)!.Value, 6);
    }

    [Fact]
    public void Event_stamp_across_wrap()
    {
        var c = new WatchClock { OffsetDriftPerSecond = 0 };
        c.Observe(10, 100.0, true);
        Assert.Equal(100.0 - 0.020, c.ToPcTime(65526)!.Value, 6);
    }

    [Fact]
    public void Detects_out_of_order_samples()
    {
        var c = new WatchClock();
        c.Observe(500, 1, true);
        Assert.True(c.IsStale(490));
        Assert.False(c.IsStale(510));
        c.Observe(65530, 1, true);
        Assert.False(c.IsStale(5)); // newer across the wrap
    }

    [Fact]
    public void No_conversion_before_sync()
    {
        Assert.Null(new WatchClock().ToPcTime(100));
    }
}
