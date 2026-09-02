using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LPS
{
    public class ShockTower : MonoBehaviour
    {

        [SerializeField] float attackCooldown = 4;

        [SerializeField] GameObject projectileGO;
        [SerializeField] Transform projectileLOC;

        float _lastAttackTime;
        GameObject currentProjectile;

        // Start is called before the first frame update
        void Start()
        {

        }

        // Update is called once per frame
        void Update()
        {
            if (Time.time - _lastAttackTime < attackCooldown) return;

            if (currentProjectile)
            {
                Destroy(currentProjectile);
            }
            currentProjectile = Instantiate(projectileGO, projectileLOC.position, Quaternion.identity, projectileLOC);

            _lastAttackTime = Time.time;
        }

    }
}
