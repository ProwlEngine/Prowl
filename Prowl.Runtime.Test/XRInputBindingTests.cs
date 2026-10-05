// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Echo;
using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
/// Tests for headset controller bindings in the input action system: they survive a save and load, and with no
/// headset running they read as released and zero instead of throwing, alone or inside composites.
/// </summary>
public class XRInputBindingTests
{
    [Fact]
    public void XRBindings_SurviveSaveAndLoad()
    {
        var map = new InputActionMap("XR");
        map.AddAction("Jump").AddBinding(XRHand.Left, XRButton.Secondary);
        InputAction grab = map.AddAction("Grab", InputActionType.Value);
        grab.AddBinding(InputBinding.CreateXRAxisBinding(XRHand.Right, XRAxis.Grip));
        InputAction move = map.AddAction("Move", InputActionType.Value);
        move.ExpectedValueType = typeof(Float2);
        move.AddBinding(InputBinding.CreateXRStickBinding(XRHand.Left));

        EchoObject saved = EchoObject.NewCompound();
        map.Serialize(ref saved, new SerializationContext());
        var loaded = new InputActionMap("Loaded");
        loaded.Deserialize(saved, new SerializationContext());

        InputBinding jump = loaded.GetAction("Jump").Bindings[0];
        Assert.Equal(InputBindingType.XRButton, jump.BindingType);
        Assert.Equal(XRHand.Left, jump.XRHand);
        Assert.Equal(XRButton.Secondary, jump.XRButton);

        InputBinding grip = loaded.GetAction("Grab").Bindings[0];
        Assert.Equal(InputBindingType.XRAxis, grip.BindingType);
        Assert.Equal(XRHand.Right, grip.XRHand);
        Assert.Equal(XRAxis.Grip, grip.XRAxis);

        InputBinding stick = loaded.GetAction("Move").Bindings[0];
        Assert.Equal(InputBindingType.XRStick, stick.BindingType);
        Assert.Equal(XRHand.Left, stick.XRHand);
    }

    [Fact]
    public void XRBindings_ReadReleasedWithoutAHeadset()
    {
        Assert.False(XR.IsRunning);

        var map = new InputActionMap("XR");
        InputAction jump = map.AddAction("Jump");
        jump.AddBinding(XRHand.Right, XRButton.Primary);
        InputAction fire = map.AddAction("Fire");
        fire.AddBinding(InputBinding.CreateXRAxisBinding(XRHand.Right, XRAxis.Trigger));
        InputAction trigger = map.AddAction("Trigger", InputActionType.Value);
        trigger.AddBinding(InputBinding.CreateXRAxisBinding(XRHand.Right, XRAxis.Trigger));
        InputAction move = map.AddAction("Move", InputActionType.Value);
        move.ExpectedValueType = typeof(Float2);
        move.AddBinding(InputBinding.CreateXRStickBinding(XRHand.Left));
        InputAction turn = map.AddAction("Turn", InputActionType.Value);
        turn.AddBinding(new AxisCompositeBinding(
            InputBinding.CreateXRButtonBinding(XRHand.Right, XRButton.Secondary),
            InputBinding.CreateXRAxisBinding(XRHand.Left, XRAxis.Grip)));

        map.Enable();
        map.UpdateActions(new NullInputHandler(), 0f);

        Assert.False(jump.IsPressed());
        Assert.False(fire.IsPressed());
        Assert.Equal(0f, trigger.ReadValue<float>());
        Assert.Equal(Float2.Zero, move.ReadValue<Float2>());
        Assert.Equal(0f, turn.ReadValue<float>());
    }

    [Fact]
    public void TouchBindings_SurviveSaveAndLoad_AndReadReleasedWithoutAHeadset()
    {
        var map = new InputActionMap("XR");
        InputAction rest = map.AddAction("Rest");
        rest.AddBinding(InputBinding.CreateXRTouchBinding(XRHand.Left, XRTouch.Thumbrest));

        EchoObject saved = EchoObject.NewCompound();
        map.Serialize(ref saved, new SerializationContext());
        var loaded = new InputActionMap("Loaded");
        loaded.Deserialize(saved, new SerializationContext());

        InputBinding touch = loaded.GetAction("Rest").Bindings[0];
        Assert.Equal(InputBindingType.XRTouch, touch.BindingType);
        Assert.Equal(XRHand.Left, touch.XRHand);
        Assert.Equal(XRTouch.Thumbrest, touch.XRTouch);

        loaded.Enable();
        loaded.UpdateActions(new NullInputHandler(), 0f);
        Assert.False(loaded.GetAction("Rest").IsPressed());
    }
}
