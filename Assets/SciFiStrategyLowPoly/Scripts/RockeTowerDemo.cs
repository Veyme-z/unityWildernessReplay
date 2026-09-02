using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LPS
{
    public class RockeTowerDemo : MonoBehaviour
    {

        [SerializeField] float attackCooldown = 4;
        [SerializeField] float nextRocketDelay = 0.3f;

        [SerializeField] float rocketSpeed = 5;

        [SerializeField] Transform[] rocketGO;

        float _lastAttackTime;
        int lastRocketIndex = 0;

        // Start is called before the first frame update
        void Start()
        {

        }

        void FixedUpdate()
        {
            if (Time.time - _lastAttackTime > attackCooldown + (nextRocketDelay * lastRocketIndex))
            {
                rocketGO[lastRocketIndex].localPosition = Vector3.zero;
                rocketGO[lastRocketIndex].gameObject.SetActive(false);

                lastRocketIndex++;
                if (lastRocketIndex == rocketGO.Length)
                {
                    lastRocketIndex = 0;
                    _lastAttackTime = Time.time;
                }
            }
        }

        // Update is called once per frame
        void Update()
        {
            for (int i = 0; i < rocketGO.Length; i++)
            {
                if (!rocketGO[i].gameObject.activeInHierarchy)
                {
                    rocketGO[i].gameObject.SetActive(true);
                }
                else
                {
                    rocketGO[i].position += Vector3.forward * Time.deltaTime * rocketSpeed;
                }
            }
        }

    }
}
