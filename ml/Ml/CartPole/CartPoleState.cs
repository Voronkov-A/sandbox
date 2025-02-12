using System;

namespace Ml.CartPole;

internal readonly struct CartPoleState
{
    private static readonly Random Random = new();

    private const float Gravity = 9.8f;
    private const float Masscart = 1.0f;
    private const float Masspole = 0.1f;
    private const float TotalMass = Masspole + Masscart;
    private const float Length = 0.5f;
    private const float PolemassLength = Masspole * Length;
    private const float ForceMag = 10.0f;
    private const float Tau = 0.02f;
    private const float ThetaThresholdRadians = (float)(12 * 2 * Math.PI / 360); // Angle at which to fail the episode
    private const float XThreshold = 2.4f;

    public CartPoleState()
    {
        X = (Random.NextSingle() * 0.1f) - 0.05f;
        XDot = (Random.NextSingle() * 0.1f) - 0.05f;
        Theta = (Random.NextSingle() * 0.1f) - 0.05f;
        ThetaDot = (Random.NextSingle() * 0.1f) - 0.05f;
    }

    private CartPoleState(int index, float x, float xDot, float theta, float thetaDot, bool isDone)
    {
        Index = index;
        X = x;
        XDot = xDot;
        Theta = theta;
        ThetaDot = thetaDot;
        IsDone = isDone;
    }

    public int Index { get; }

    public float X { get; }

    public float XDot { get; }

    public float Theta { get; }

    public float ThetaDot { get; }

    public bool IsDone { get; }

    public CartPoleState Apply(CartPoleAction action)
    {
        var force = action switch
        {
            CartPoleAction.MoveLeft => -ForceMag,
            CartPoleAction.MoveRight => ForceMag,
            _ => throw new InvalidOperationException()
        };

        var cosTheta = MathF.Cos(Theta);
        var sinTheta = MathF.Sin(Theta);
        var temp = (force + PolemassLength * ThetaDot * ThetaDot * sinTheta) / TotalMass;
        var thetaacc
            = (Gravity * sinTheta - cosTheta * temp)
            / (Length * (4.0f / 3.0f - Masspole * cosTheta * cosTheta / TotalMass));
        var xacc = temp - PolemassLength * thetaacc * cosTheta / TotalMass;

        var x = X + Tau * XDot;
        var xDot = XDot + Tau * xacc;
        var theta = Theta + Tau * ThetaDot;
        var thetaDot = ThetaDot + Tau * thetaacc;

        //state = np.array(x, x_dot, theta, theta_dot);
        var done = x < -XThreshold || x > XThreshold || theta < -ThetaThresholdRadians || theta > ThetaThresholdRadians;
        /*float reward;
        if (!done)
        {
            reward = 1.0f;
        }
        else if (steps_beyond_done == -1)
        {
            // Pole just fell!
            steps_beyond_done = 0;
            reward = 1.0f;
        }
        else
        {
            if (steps_beyond_done == 0)
            {
                Console.WriteLine("You are calling 'step()' even though this environment has already returned done = True. You should always call 'reset()' once you receive 'done = True' -- any further steps are undefined behavior.");
                //todo logging: logger.warn("You are calling 'step()' even though this environment has already returned done = True. You should always call 'reset()' once you receive 'done = True' -- any further steps are undefined behavior.");
            }

            steps_beyond_done += 1;
            reward = 0.0f;
        }

        return new Step(state, reward, done, null);*/

        return new CartPoleState(Index + 1, x, xDot, theta, thetaDot, done);
    }
}
