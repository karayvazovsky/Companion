namespace Companion.Core;

/// <summary>Damped swing in degrees (+ = clockwise, i.e. the body swings left). The body lags behind the grip's motion.</summary>
public sealed class Pendulum
{
    public const double MaxAngle = 35;
    private const double Stiffness = 60;
    private const double Damping = 9;
    private const double DegreesPerDipPerSecond = 0.03;
    private double _velocity;

    public double Angle { get; private set; }

    public void Step(double seconds, double pivotVelocityX, double restAngle = 0)
    {
        var dt = Math.Clamp(seconds, 0, 0.1);
        var target = Math.Clamp(restAngle + pivotVelocityX * DegreesPerDipPerSecond, -MaxAngle, MaxAngle);
        _velocity += (Stiffness * (target - Angle) - Damping * _velocity) * dt;
        Angle = Math.Clamp(Angle + _velocity * dt, -MaxAngle, MaxAngle);
    }

    public void Reset()
    {
        Angle = 0;
        _velocity = 0;
    }

    /// <summary>Gentle idle swing: ±6°, one full swing every 2 s.</summary>
    public static double IdleSwing(double seconds) => 6 * Math.Sin(seconds * Math.PI);
}
