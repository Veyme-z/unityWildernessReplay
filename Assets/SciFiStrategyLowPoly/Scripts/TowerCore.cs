using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LPS
{
    public class TowerCore : MonoBehaviour
    {
        [SerializeField] float attackCooldown = 4;

        [SerializeField] ParticleSystem[] VFX;

        // Start is called before the first frame update
        void Start()
        {

        }

        // Update is called once per frame
        void Update()
        {
        }

        public void Fire(int gunIndex)
        {
            if (VFX.Length > gunIndex)
            {
                VFX[gunIndex].Play();
            }
        }

    }
}
