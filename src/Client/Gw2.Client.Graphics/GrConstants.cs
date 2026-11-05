using System.Numerics;
using Crystal.Graphics;

namespace Gw2.Client.Graphics;

public sealed class GrConstants
{
    private readonly Vector4[] _registers = new Vector4[(int)GrConstantRegister.Count];

    public void SetWorld(Matrix4x4 world)
    {
        var register = (int)GrConstantRegister.World;
        _registers[register + 0] = new Vector4(world.M11, world.M21, world.M31, world.M41);
        _registers[register + 1] = new Vector4(world.M12, world.M22, world.M32, world.M42);
        _registers[register + 2] = new Vector4(world.M13, world.M23, world.M33, world.M43);
    }

    public void SetLight(Vector3 direction, Vector3 color)
    {
        _registers[(int)GrConstantRegister.LightDirection] = new Vector4(direction.X, direction.Y, direction.Z, 0f);
        _registers[(int)GrConstantRegister.LightColor] = new Vector4(color.X, color.Y, color.Z, 1f);
    }

    public void SetTime(float seconds) => _registers[(int)GrConstantRegister.Time] = new Vector4(seconds, 0f, 0f, 0f);

    public void Apply(ShaderProgram program, Matrix4x4 viewProjection)
    {
        program.Parameters["Registers"]?.SetValue(_registers);
        program.Parameters["ViewProjection"]?.SetValue(viewProjection);
    }
}
