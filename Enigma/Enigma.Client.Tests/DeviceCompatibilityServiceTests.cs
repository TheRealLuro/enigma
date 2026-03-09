using Enigma.Client.Models;
using Enigma.Client.Services;
using Xunit;

namespace Enigma.Client.Tests;

public sealed class DeviceCompatibilityServiceTests
{
    [Theory]
    [MemberData(nameof(ClassificationCases))]
    public void Classify_ReturnsExpectedClass(DeviceCapabilitySnapshot snapshot, DeviceCompatibilityClass expected)
    {
        var actual = DeviceCompatibilityService.Classify(snapshot);
        Assert.Equal(expected, actual);
    }

    public static IEnumerable<object[]> ClassificationCases()
    {
        yield return
        [
            new DeviceCapabilitySnapshot
            {
                ViewportWidth = 420,
                PrimaryPointerFine = true,
                CanHover = true,
            },
            DeviceCompatibilityClass.DesktopPlayable,
        ];

        yield return
        [
            new DeviceCapabilitySnapshot
            {
                ViewportWidth = 390,
                HasTouch = true,
                MaxTouchPoints = 5,
                PrimaryPointerCoarse = true,
                AnyCoarsePointer = true,
                UserAgentMobile = true,
            },
            DeviceCompatibilityClass.MobileBrowseOnly,
        ];

        yield return
        [
            new DeviceCapabilitySnapshot
            {
                ViewportWidth = 1024,
                HasTouch = true,
                MaxTouchPoints = 10,
                PrimaryPointerCoarse = true,
                AnyCoarsePointer = true,
            },
            DeviceCompatibilityClass.TabletBrowseOnly,
        ];

        yield return
        [
            new DeviceCapabilitySnapshot
            {
                ViewportWidth = 1280,
                HasTouch = true,
                MaxTouchPoints = 10,
                AnyFinePointer = true,
                CanHover = true,
            },
            DeviceCompatibilityClass.DesktopPlayable,
        ];

        yield return
        [
            new DeviceCapabilitySnapshot
            {
                ViewportWidth = 1200,
            },
            DeviceCompatibilityClass.UnknownFallback,
        ];
    }
}
