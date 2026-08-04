using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public class PlayerWeapon : MonoBehaviour
{
    public Transform bulletSpawnPoint;
    public PlayerWeaponBullet bulletEffectPrefab;
    public GameObject bulletSparkPrefab;
    public float bulletInterval = 0.15f;
    private float lastFireTime;

    public void Fire(Vector3 targetPos, bool dealDamage = true)
    {
        if (Time.time - lastFireTime < bulletInterval)
        {
            return;
        }
        lastFireTime = Time.time;
        Vector3 direction = targetPos - bulletSpawnPoint.position;
        direction.Normalize();
        PlayerWeaponBullet bulletEffect = Instantiate(bulletEffectPrefab, bulletSpawnPoint.position, quaternion.identity);
        GameObject spark = Instantiate(bulletSparkPrefab, bulletSpawnPoint.position, Quaternion.identity);
        spark.transform.forward = direction;

        bulletEffect.transform.forward = direction;
        bulletEffect.dealDamage = dealDamage;
    }
}
