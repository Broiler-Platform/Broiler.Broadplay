// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0

using Broiler.Graphics.Geometry;

namespace Broiler.Broadplay.Windows.Tests;

public sealed class BroadplayToolbarTests
{
    [Fact]
    public void StartsInMediaWorkspaceAndRejectsDocuments()
    {
        using var host = new BroadplayUiHost(() => new BSize(420, 300), () => 2, () => { }, _ => { });
        using var app = new BroadplayApp(host, () => { });
        app.RenderFrame();
        Assert.Equal(BroadplayViewKind.Empty, app.CurrentView);
        Assert.Equal("Open media", app.OpenButton!.Text);
        Assert.False(app.Open("example.pdf", "%PDF-1.7"u8));
        Assert.Contains("Use Plate", app.LastAction);
        Assert.Equal(BroadplayViewKind.Empty, app.CurrentView);
        Assert.False(app.Menu.CommandDispatcher!.TryExecute("file.print"));
        Assert.False(app.Menu.CommandDispatcher.TryExecute("file.scan"));
    }

    [Fact]
    public void OpenButton_Invokes_RequestOpenFile_WhenProvided()
    {
        bool openFileRequested = false;
        using var host = new BroadplayUiHost(() => new BSize(1200, 800), () => 1, () => { }, _ => { });
        using var app = new BroadplayApp(host, () => { }, requestOpenFile: () => openFileRequested = true);

        app.OpenButton!.Click();

        Assert.True(openFileRequested);
    }

    [Fact]
    public void OpenFolderButton_Invokes_RequestOpenFolder_WhenProvided()
    {
        bool openFolderRequested = false;
        using var host = new BroadplayUiHost(() => new BSize(1200, 800), () => 1, () => { }, _ => { });
        using var app = new BroadplayApp(host, () => { }, requestOpenFolder: () => openFolderRequested = true);

        app.OpenFolderButton!.Click();

        Assert.True(openFolderRequested);
    }

    [Fact]
    public void BroadplayFileFormats_CreateAcceptExtensions_ProducesCommaSeparatedExtensions()
    {
        var formats = BroadplayFileFormats.CreateDefault();
        string accept = formats.CreateAcceptExtensions();

        Assert.Contains(".wav", accept);
        Assert.Contains(".mp4", accept);
        Assert.DoesNotContain(".md", accept);
        Assert.DoesNotContain(".png", accept);
        Assert.DoesNotContain(".jpg", accept);
        Assert.DoesNotContain("*", accept);
        Assert.DoesNotContain(";", accept);
    }

}
