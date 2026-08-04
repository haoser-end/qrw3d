using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerWeaponBullet : MonoBehaviour
{
    public int damage = 10;
    public Rigidbody rb;
    public float flyPower = 30f;
    public float lifeTime = 10f;
    [HideInInspector] public bool dealDamage = true; // 网络同步时远程客户端设 false

    private Vector3 prevPosition;

    public GameObject trailEffect;
    public float trailInterval = 0.1f;
    private float trailInterval_timer = 0;
    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }
    private void Start()
    {
        rb.velocity = transform.forward * flyPower;
        Destroy(gameObject, lifeTime);
        prevPosition = transform.position;
        CheckInitalOverlap();
    }

    private void Update()
    {
        CheckCollision();
        prevPosition = transform.position;
        trailInterval_timer+= Time.deltaTime;
        if(trailInterval_timer > trailInterval)
        {
            SpawnTrailEffect();
            trailInterval_timer= 0;
        }
    }
    private void SpawnTrailEffect()
    {
        if (trailEffect != null)
        {
            Quaternion reverseRotation = Quaternion.LookRotation(-transform.forward);
            Destroy(Instantiate(trailEffect, transform.position, reverseRotation),2);
        }
    }
    void CheckInitalOverlap()
    {
        if (!dealDamage) return;
        Collider[] colliders = Physics.OverlapSphere(transform.position, 0.1f);
        foreach (var collider in colliders)
        {
            EnemyBase enemy = collider.GetComponent<EnemyBase>();
            if (enemy != null)
            {
                enemy.Hurt(this, 1);
                Destroy(gameObject);
                return;
            }
        }
    }

    void CheckCollision()
    {
        RaycastHit hit;
        Vector3 dir = transform.position - prevPosition;
        float distnace = Vector3.Distance(transform.position, prevPosition);

        if (Physics.Raycast(prevPosition, dir.normalized, out hit, distnace))
        {
            if (hit.collider.CompareTag("Enemy"))
            {
                if (dealDamage)
                {
                    EnemyBase enemy = hit.collider.GetComponent<EnemyBase>();
                    enemy.Hurt(this, 1);
                }
                Destroy(gameObject);
            }
        }
    }
}
