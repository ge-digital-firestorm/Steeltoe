// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using Microsoft.Extensions.Logging;
using System;
using Xunit;

namespace Steeltoe.Common.Availability.Test;

public class ApplicationAvailabilityTest
{
    private readonly ILogger<ApplicationAvailability> _logger = TestHelpers.GetLoggerFactory().CreateLogger<ApplicationAvailability>();

    [Fact]
    public void TracksAndReturnsState()
    {
        var availability = new ApplicationAvailability(_logger);

        availability.SetAvailabilityState("Test", LivenessState.Broken, GetType().Name);
        availability.SetAvailabilityState(availability.LivenessKey, LivenessState.Correct, GetType().Name);
        availability.SetAvailabilityState(availability.ReadinessKey, ReadinessState.AcceptingTraffic, GetType().Name);

        Assert.Equal(LivenessState.Broken, availability.GetAvailabilityState("Test"));
        Assert.Equal(LivenessState.Correct, availability.GetLivenessState());
        Assert.Equal(ReadinessState.AcceptingTraffic, availability.GetReadinessState());
    }

    [Fact]
    public void ReturnsNullOnInit()
    {
        var availability = new ApplicationAvailability(_logger);

        var liveness = availability.GetLivenessState();
        var readiness = availability.GetReadinessState();

        Assert.Null(liveness);
        Assert.Null(readiness);
    }

    [Fact]
    public void KnownTypesRequireMatchingType()
    {
        var availability = new ApplicationAvailability(_logger);

        Assert.Throws<InvalidOperationException>(() => availability.SetAvailabilityState(availability.LivenessKey, ReadinessState.AcceptingTraffic, null));
        Assert.Throws<InvalidOperationException>(() => availability.SetAvailabilityState(availability.ReadinessKey, LivenessState.Correct, null));
    }

    [Fact]
    public void FiresEventsOnKnownTypeStateChange()
    {
        var availability = new ApplicationAvailability(_logger);
        availability.LivenessChanged += Availability_LivenessChanged;
        availability.ReadinessChanged += Availability_ReadinessChanged;

        availability.SetAvailabilityState(availability.LivenessKey, LivenessState.Broken, null);
        Assert.Equal(LivenessState.Broken, _lastLivenessState);
        availability.SetAvailabilityState(availability.ReadinessKey, ReadinessState.RefusingTraffic, null);
        Assert.Equal(ReadinessState.RefusingTraffic, _lastReadinessState);
        availability.SetAvailabilityState(availability.LivenessKey, LivenessState.Correct, null);
        availability.SetAvailabilityState(availability.ReadinessKey, ReadinessState.AcceptingTraffic, null);

        Assert.Equal(2, _livenessChanges);
        Assert.Equal(LivenessState.Correct, _lastLivenessState);
        Assert.Equal(2, _readinessChanges);
        Assert.Equal(ReadinessState.AcceptingTraffic, _lastReadinessState);
    }

    private int _livenessChanges;
    private LivenessState _lastLivenessState;

    private int _readinessChanges;
    private ReadinessState _lastReadinessState;

    private void Availability_ReadinessChanged(object sender, EventArgs e)
    {
        _readinessChanges++;
        _lastReadinessState = (ReadinessState)(e as AvailabilityEventArgs).NewState;
    }

    private void Availability_LivenessChanged(object sender, EventArgs e)
    {
        _livenessChanges++;
        _lastLivenessState = (LivenessState)(e as AvailabilityEventArgs).NewState;
    }
}
