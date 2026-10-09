using UnityEngine;
using UnityEngine.InputSystem;

namespace MyGame.Foundation
{
    [RequireComponent(typeof(CharacterController))]
    public sealed class FirstPersonMotor : MonoBehaviour
    {
        public Transform view;
        public bool InputEnabled { get; set; }
        public float sensitivity = .12f;
        public float speed = 4, jumpHeight = 1.1f;
        private CharacterController controller;
        private float pitch, vertical;
        private void Awake() { controller = GetComponent<CharacterController>(); }
        private void Update()
        {
            if (!InputEnabled || view == null) return;
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            if (mouse != null)
            {
                var look = mouse.delta.ReadValue() * sensitivity;
                transform.Rotate(0, look.x, 0);
                pitch = Mathf.Clamp(pitch - look.y, -85, 85);
                view.localRotation = Quaternion.Euler(pitch, 0, 0);
            }
            Vector2 move = Vector2.zero;
            if (keyboard != null)
            {
                move.x = (keyboard.dKey.isPressed ? 1 : 0) - (keyboard.aKey.isPressed ? 1 : 0);
                move.y = (keyboard.wKey.isPressed ? 1 : 0) - (keyboard.sKey.isPressed ? 1 : 0);
            }
            if (controller.isGrounded)
            {
                vertical = -2;
                if (keyboard != null && keyboard.spaceKey.wasPressedThisFrame) vertical = Mathf.Sqrt(jumpHeight * 19.62f);
            }
            vertical -= 9.81f * Time.deltaTime;
            Vector3 motion = (transform.right * move.x + transform.forward * move.y).normalized * speed;
            controller.Move((motion + Vector3.up * vertical) * Time.deltaTime);
        }
    }
}
