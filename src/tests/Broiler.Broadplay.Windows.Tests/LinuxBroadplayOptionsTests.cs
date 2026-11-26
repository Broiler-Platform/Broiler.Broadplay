// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0

using Xunit;

namespace Broiler.Broadplay.Windows.Tests;

public sealed class LinuxBroadplayOptionsTests
{
    [Fact]
    public void Default_Options_Are_Sensible()
    {
        var options = LinuxBroadplayOptions.Parse([]);

        Assert.Equal(1120, options.Width);
        Assert.Equal(780, options.Height);
        Assert.Null(options.ArtifactDirectory);
        Assert.Null(options.InitialPath);
        Assert.False(options.ShowHelp);
    }

    [Fact]
    public void Flag_Options_Are_Parsed_Correctly()
    {
        var options = LinuxBroadplayOptions.Parse([
            "--offscreen",
            "--no-input",
            "--duration-ms=2500",
            "--size=1920x1080",
            "--artifact-dir=/tmp/test-output",
            "--input-ignore-focus",
            "/path/to/sample.pdf",
        ]);

        Assert.False(options.OpenWindow);
        Assert.False(options.EnableEvdevInput);
        Assert.Equal(2500, options.DurationMilliseconds);
        Assert.Equal(1920, options.Width);
        Assert.Equal(1080, options.Height);
        Assert.Equal("/tmp/test-output", options.ArtifactDirectory);
        Assert.True(options.IgnoreInputFocus);
        Assert.Equal("/path/to/sample.pdf", options.InitialPath);
    }

    [Fact]
    public void Size_Options_Are_Clamped_To_Limits()
    {
        var optionsTooSmall = LinuxBroadplayOptions.Parse(["--width=100", "--height=50"]);
        Assert.Equal(640, optionsTooSmall.Width);
        Assert.Equal(480, optionsTooSmall.Height);

        var optionsTooLarge = LinuxBroadplayOptions.Parse(["--size=5000x3000"]);
        Assert.Equal(3840, optionsTooLarge.Width);
        Assert.Equal(2160, optionsTooLarge.Height);
    }

    [Theory]
    [InlineData("-h")]
    [InlineData("--help")]
    public void Help_Flag_Sets_ShowHelp(string flag)
    {
        var options = LinuxBroadplayOptions.Parse([flag]);
        Assert.True(options.ShowHelp);
    }

    [Fact]
    public void Stage_Flag_Enables_StageMode_Without_Consuming_Path()
    {
        var defaults = LinuxBroadplayOptions.Parse([]);
        Assert.False(defaults.StageMode);

        var stage = LinuxBroadplayOptions.Parse(["--stage", "/media/film.mp4"]);
        Assert.True(stage.StageMode);
        Assert.Equal("/media/film.mp4", stage.InitialPath);
    }
}
