using UnityEngine;
using UnityEngine.InputSystem;

// 방향키로 공을 굴리고 스페이스바로 점프한다.
[RequireComponent(typeof(Rigidbody))]
public class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    public float moveForce = 12f;
    public float maxSpeed = 6f;

    [Header("Jump")]
    public float jumpVelocity = 6.5f;   // 약 2유닛 높이까지 뜀 (박스 높이 1)
    public float groundCheckExtra = 0.1f;

    [Header("Respawn")]
    public float fallLimitY = -5f;

    Rigidbody rb;
    float radius;
    Vector3 spawnPosition;
    Vector2 moveInput;
    bool jumpRequested;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        radius = GetComponent<SphereCollider>() != null
            ? GetComponent<SphereCollider>().radius * transform.localScale.y
            : 0.5f;
        spawnPosition = transform.position;
    }

    void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null) return;

        moveInput = Vector2.zero;
        if (keyboard.leftArrowKey.isPressed) moveInput.x -= 1f;
        if (keyboard.rightArrowKey.isPressed) moveInput.x += 1f;
        if (keyboard.downArrowKey.isPressed) moveInput.y -= 1f;
        if (keyboard.upArrowKey.isPressed) moveInput.y += 1f;

        if (keyboard.spaceKey.wasPressedThisFrame) jumpRequested = true;

        if (transform.position.y < fallLimitY) Respawn();
    }

    void FixedUpdate()
    {
        Vector3 move = new Vector3(moveInput.x, 0f, moveInput.y).normalized;
        Vector3 horizontal = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
        if (horizontal.magnitude < maxSpeed || Vector3.Dot(horizontal, move) <= 0f)
            rb.AddForce(move * moveForce, ForceMode.Force);

        if (jumpRequested)
        {
            jumpRequested = false;
            if (IsGrounded())
            {
                Vector3 v = rb.linearVelocity;
                v.y = 0f;
                rb.linearVelocity = v;
                rb.AddForce(Vector3.up * jumpVelocity, ForceMode.VelocityChange);
            }
        }
    }

    bool IsGrounded()
    {
        // 바닥과 박스 윗면 모두 착지로 인정
        return Physics.SphereCast(transform.position, radius * 0.9f, Vector3.down, out _,
            radius * 0.1f + groundCheckExtra, ~0, QueryTriggerInteraction.Ignore);
    }

    void Respawn()
    {
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        transform.position = spawnPosition;
    }
}
