using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LPS
{
    public class ShockProjectileDemo : MonoBehaviour
    {
        [SerializeField] float projectileSpeed = 35;

        // Start is called before the first frame update
        void Start()
        {

        }

        // Update is called once per frame
        void Update()
        {
            transform.position += Vector3.forward * Time.deltaTime * projectileSpeed;
        }
    }
}
