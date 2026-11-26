// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0

using Broiler.Graphics.Geometry;
using Broiler.UI;

namespace Broiler.Broadplay.Windows.Tests;

public sealed class BroadplayTooltipTests
{
    [Fact]
    public void Controls_And_Toolbar_Have_Tooltips()
    {
        using var host = new BroadplayUiHost(() => new BSize(1200, 800), () => 1, () => { }, _ => { });
        bool ticking = false;
        using var app = new BroadplayApp(host, () => { }, setTicking: t => ticking = t);
        app.RenderFrame();

        Assert.Equal("Media actions", app.Toolbar.ToolTipText);
        Assert.False(app.WantsTick);
        Assert.False(ticking);
    }
}
