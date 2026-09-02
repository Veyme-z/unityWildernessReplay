using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LPS
{
    public class EffectTriggerDemo : MonoBehaviour
    {
        [SerializeField] float attackCooldown = 4;

        [SerializeField] ParticleSystem[] VFX;

        float _lastAttackTime;

        // Start is called before the first frame update
        void Start()
        {

        }

        // Update is called once per frame
        void Update()
        {
            if (Time.time - _lastAttackTime < attackCooldown) return;

            foreach (var item in VFX)
            {
                item.Play();
            }

            _lastAttackTime = Time.time;
        }
    }
}
