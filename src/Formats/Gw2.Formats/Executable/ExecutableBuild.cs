namespace Gw2.Formats;

public static class ExecutableBuild
{
    public static int FromExecutable(string path) => ExecutableIdentity.Build(ExecutableImage.Open(path));
}
