using Broiler.Graphics.Geometry;
using Broiler.Graphics.Imaging;
using Broiler.Input;
using Broiler.Input.Keyboard;
using Broiler.Media;
using Broiler.Media.Image.Managed;
using Broiler.UI;
using Broiler.UI.Dialog.Standard;

namespace Broiler.Broadplay.Windows.Tests;

public sealed class BroadplayGalleryTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "broadplay-gallery-" + Guid.NewGuid().ToString("N"));
    private readonly BroadplayUiHost _host;
    private readonly BroadplayApp _app;

    public BroadplayGalleryTests()
    {
        Directory.CreateDirectory(_folder);
        _host = new BroadplayUiHost(() => new BSize(1120, 780), () => 1, () => { }, _ => { });
        _app = new BroadplayApp(_host, () => { });
        _app.RenderFrame();
    }

    [Fact]
    public void Folder_Menu_Requires_Confirmation_And_Cancel_Preserves_The_View()
    {
        string document = Path.Combine(_folder, "tone.mp3");
        File.WriteAllBytes(document, LoadMp3Fixture());
        Assert.True(_app.OpenFile(document));
        Assert.True(_app.Menu.CommandDispatcher!.TryExecute("file.open-folder"));
        var dialog = Assert.IsType<StandardDialog>(_app.Session.ModalElement);
        var picker = Assert.IsType<BroadplayFolderDialog>(Assert.Single(dialog.Children));
        Assert.True(picker.Navigate(_folder));
        Assert.Equal(BroadplayViewKind.Audio, _app.CurrentView);
        dialog.Cancel();
        Assert.Equal(BroadplayViewKind.Audio, _app.CurrentView);

        Assert.True(_app.Menu.CommandDispatcher.TryExecute("file.open-folder"));
        dialog = Assert.IsType<StandardDialog>(_app.Session.ModalElement);
        picker = Assert.IsType<BroadplayFolderDialog>(Assert.Single(dialog.Children));
        picker.PathEdit.Text = Path.Combine(_folder, "missing");
        Assert.False(picker.AcceptFolder());
        Assert.True(dialog.IsPresented);
        picker.PathEdit.Text = _folder;
        Assert.True(picker.AcceptFolder());
        Assert.Equal(BroadplayViewKind.Gallery, _app.CurrentView);
        Assert.Equal("tone.mp3", Assert.Single(_app.Gallery.Entries).Name);
        Assert.Same(_app.Gallery, _app.Session.FocusedElement);
    }

    [Fact]
    public void Folder_Lists_All_Immediate_Entries_Folders_First_And_Leaves_Failed_Opens_Intact()
    {
        Directory.CreateDirectory(Path.Combine(_folder, "Subfolder"));
        File.WriteAllText(Path.Combine(_folder, "z.unknown"), "unsupported");
        File.WriteAllBytes(Path.Combine(_folder, "A.mp3"), LoadMp3Fixture());
        File.WriteAllBytes(Path.Combine(_folder, "Subfolder", "nested.mp3"), LoadMp3Fixture());
        Assert.True(_app.OpenFolder(_folder));
        Assert.Equal(new[] { "Subfolder", "A.mp3" }, _app.Gallery.Entries.Select(e => e.Name));
        var entries = _app.Gallery.Entries;
        Assert.False(_app.OpenFolder(Path.Combine(_folder, "missing")));
        Assert.Same(entries, _app.Gallery.Entries);
        Assert.Equal(BroadplayViewKind.Gallery, _app.CurrentView);
        Assert.False(_app.OpenFile(Path.Combine(_folder, "z.unknown")));
        Assert.Equal(BroadplayViewKind.Gallery, _app.CurrentView);
        _app.RenderFrame();
        _app.Dispatch(Key("Enter"));
        Assert.Equal("nested.mp3", Assert.Single(_app.Gallery.Entries).Name);
    }

    [Fact]
    public void Keyboard_Browsing_Scrolls_And_Opening_A_File_Can_Return_To_The_Gallery()
    {
        byte[] mp3 = LoadMp3Fixture();
        for (int i = 0; i < 80; i++)
            File.WriteAllBytes(Path.Combine(_folder, $"{i:000}.mp3"), mp3);
        Assert.True(_app.OpenFolder(_folder));
        _app.RenderFrame();
        _app.Dispatch(Key("End"));
        Assert.Equal(79, _app.Gallery.SelectedIndex);
        Assert.True(_app.Gallery.ScrollOffset > 0);
        double scroll = _app.Gallery.ScrollOffset;
        _app.Dispatch(Key("Enter"));
        Assert.Equal(BroadplayViewKind.Audio, _app.CurrentView);
        Assert.True(_app.Menu.CommandDispatcher!.TryExecute("view.gallery"));
        Assert.Equal(BroadplayViewKind.Gallery, _app.CurrentView);
        Assert.Equal(scroll, _app.Gallery.ScrollOffset);
        Assert.Equal(79, _app.Gallery.SelectedIndex);
        Assert.True(_app.Menu.CommandDispatcher.TryExecute("file.close"));
        Assert.Equal(BroadplayViewKind.Empty, _app.CurrentView);
        Assert.Empty(_app.Gallery.Entries);
        Assert.False(_app.Menu.CommandDispatcher.TryExecute("view.gallery"));
    }

    [Fact]
    public void Empty_Folder_Renders_And_Thumbnail_Decoding_Handles_Corrupt_And_Misnamed_Files()
    {
        Assert.True(_app.OpenFolder(_folder));
        _app.RenderFrame();
        Assert.Empty(_app.Gallery.Entries);
        BImageCodecs.Use(new MediaCodecCatalog(ManagedImageCodecs.CreateCodecs()));
        string image = Path.Combine(_folder, "picture.bin");
        using (var source = new BBitmap(800, 400))
            source.Save(image);
        byte[] bytes = Assert.IsType<byte[]>(BroadplayGallery.CreateThumbnail(image, CancellationToken.None));
        using BBitmap thumbnail = BBitmap.Decode(bytes);
        Assert.Equal(320, thumbnail.Width);
        Assert.Equal(160, thumbnail.Height);
        Assert.Null(BroadplayGallery.CreateThumbnail(image, new CancellationToken(true)));
        string corrupt = Path.Combine(_folder, "broken.png");
        File.WriteAllText(corrupt, "not an image");
        Assert.Null(BroadplayGallery.CreateThumbnail(corrupt, CancellationToken.None));
        Assert.Null(BroadplayGallery.CreateThumbnail(Path.Combine(_folder, "missing.png"), CancellationToken.None));
    }

    private static byte[] LoadMp3Fixture() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "mpeg1-mono-long.mp3"));

    private static UiInputEvent Key(string key) => UiInputEvent.FromKeyboardKey(new KeyboardKeyEvent(
        new InputEventHeader(InputDeviceId.FromOpaqueValue("gallery-test"), new InputTimestamp(1, 1000, "test"), 1),
        KeyboardKey.FromName(key), KeyboardKeyTransition.Down, KeyboardModifierState.None, 0, 0, 1, false, false));

    public void Dispose()
    {
        _app.Dispose();
        _host.Dispose();
        Directory.Delete(_folder, recursive: true);
    }
}
