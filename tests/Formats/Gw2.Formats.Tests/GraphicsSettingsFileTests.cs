using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Formats.Tests;

[TestClass]
public sealed class GraphicsSettingsFileTests
{
    [TestMethod]
    public void ReadsTheOptionsTheGameSaved()
    {
        var path = Gw2Installation.GraphicsSettingsPath();
        if (path is null) Assert.Inconclusive("The game has not saved graphics settings.");
        var settings = GraphicsSettingsFile.Parse(File.ReadAllText(path!));

        foreach (var name in new[] { "shadowsPreset", "reflections", "textureDetail", "lodDistance", "sampling", "shaders", "frameLimit", "shadowsResolution", "shadowsCascadeCount" })
            Assert.IsNotNull(settings.EnumIndexOf(name), name);
        foreach (var name in new[] { "bestTextureFiltering", "depthBlur", "verticalSync", "screenspaceShadows" })
            Assert.IsNotNull(settings.BoolOf(name), name);
        Assert.IsNotNull(settings.FloatOf("gamma"));
        Assert.IsNotNull(settings.Resolution);
    }

    [TestMethod]
    public void IndexesEnumsInTheOrderTheGameRegisteredThem()
    {
        var settings = GraphicsSettingsFile.Parse("""
            <GSA_SDK><GAMESETTINGS>
              <OPTION Name="shadowsPreset" Type="Enum" Value="ultra"><ENUM EnumValue="off"/><ENUM EnumValue="low"/><ENUM EnumValue="medium"/><ENUM EnumValue="high"/><ENUM EnumValue="ultra"/></OPTION>
              <OPTION Name="gamma" Type="Float" Value="1.25"/>
              <OPTION Name="verticalSync" Type="Bool" Value="true"/>
            </GAMESETTINGS></GSA_SDK>
            """);

        Assert.AreEqual(4, settings.EnumIndexOf("shadowsPreset"));
        Assert.AreEqual(1.25f, settings.FloatOf("gamma"));
        Assert.AreEqual(true, settings.BoolOf("verticalSync"));
        Assert.IsNull(settings.EnumIndexOf("sampling"));
    }
}
