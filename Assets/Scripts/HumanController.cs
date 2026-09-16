using UnityEngine;

// Slot 0 = keyboard, slots 1-4 = gamepads. All buttons come from Bindings (rebindable).
public class HumanController : IRobotController
{
    public readonly int slot;

    public HumanController(int slot) { this.slot = slot; }

    public static string SlotName(int slot) => slot == 0 ? "Keyboard" : $"Gamepad {slot}";

    public bool HumanPlayerPressed() => Bindings.Down(Act.HumanPlayer, slot);

    public RobotCommand Tick(Robot r)
    {
        // A driver never aims at a fixed point: drop anything left over from the AUTO routine so the
        // robot always targets whichever HIVE CELL is up right now.
        r.aiTarget = null;
        var c = new RobotCommand();
        Vector2 move;
        float rotate;
        if (slot == 0)
        {
            move = new Vector2(
                (Bindings.Held(Act.MoveRight, 0) ? 1 : 0) - (Bindings.Held(Act.MoveLeft, 0) ? 1 : 0),
                (Bindings.Held(Act.MoveForward, 0) ? 1 : 0) - (Bindings.Held(Act.MoveBack, 0) ? 1 : 0));
            if (move.sqrMagnitude > 1) move.Normalize();
            rotate = (Bindings.Held(Act.RotateRight, 0) ? 1 : 0) - (Bindings.Held(Act.RotateLeft, 0) ? 1 : 0);
        }
        else
        {
            move = Bindings.Stick(slot, false);
            rotate = Bindings.Stick(slot, true).x;
            if (Bindings.Held(Act.RotateRight, slot)) rotate = 1;
            if (Bindings.Held(Act.RotateLeft, slot)) rotate = -1;
        }

        c.intake = Bindings.Held(Act.Intake, slot);
        c.outtake = Bindings.Held(Act.Outtake, slot);
        c.shoot = Bindings.Held(Act.Fire, slot);
        c.feed = Bindings.Held(Act.Feed, slot);
        c.flower = Bindings.Held(Act.FlowerMech, slot);
        if (Bindings.Consume(Act.ToggleTarget, slot)) r.targetFlower = !r.targetFlower;

        DriveCameraRelative(r, ref c, move, rotate);
        return c;
    }

    // "Forward" on the stick moves the robot up the screen, whatever way it is facing.
    static void DriveCameraRelative(Robot r, ref RobotCommand c, Vector2 move, float rotate)
    {
        Vector3 fwd = CameraRig.ScreenForward();
        Vector3 right = new Vector3(fwd.z, 0, -fwd.x);
        Vector3 world = right * move.x + fwd * move.y;
        float mag = Mathf.Clamp01(world.magnitude);

        if (r.spec.drive == DriveType.Mecanum)
        {
            Vector3 local = r.transform.InverseTransformDirection(world);
            c.forward = local.z;
            c.strafe = local.x;
            c.turn = rotate;
            return;
        }

        // Tank: turn towards the requested direction (or drive backwards if that is quicker).
        c.turn = rotate;
        if (mag < 0.05f) return;
        float want = Mathf.Atan2(world.x, world.z) * Mathf.Rad2Deg;
        float err = Mathf.DeltaAngle(r.transform.eulerAngles.y, want);
        float sign = 1f;
        if (Mathf.Abs(err) > 105f) { err = Mathf.DeltaAngle(0, err + 180f); sign = -1f; }
        c.turn = Mathf.Clamp(err / 35f + rotate, -1, 1);
        c.forward = sign * mag * Mathf.Clamp01(Mathf.Cos(err * Mathf.Deg2Rad) * 1.3f - 0.2f);
    }
}
