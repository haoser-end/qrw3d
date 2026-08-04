using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class EnemyHealthBar : MonoBehaviour
{
    public Image healthSlider;
    private Transform cameraTransform;
    private void Start()
    {
        cameraTransform = Camera.main.transform;
        healthSlider.fillAmount = 1;

    }
    private void Update()
    {
        Vector3 dir = cameraTransform.position - transform.position;
        transform.rotation = Quaternion.LookRotation(-dir);
    }
    public void UpdateHealthBar(float healthRatio)
    {
        healthSlider.fillAmount = healthRatio;
    }
}
