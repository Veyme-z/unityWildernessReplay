using UnityEngine;

namespace LPS
{
    public class TopDown_Camera : MonoBehaviour
    {
        // https://github.com/federicocasares/stratecam
        // Move camera with WASD or Mouse at the edge of the screen
        // Rotate camera by clicking scroll button
        // Zoom with scroll button

        // Public fields
        [Header("Zoom and Rotation")]
        public float zoomSpeed = 100.0f;
        public float minZoomDistance = 5f;
        public float maxZoomDistance = 100.0f;
        public float rotationSpeed = 50.0f;

        [Header("Screen Edge Movement")]
        public bool allowScreenEdgeMovement = true;
        public int screenEdgeSize = 100;
        public float screenEdgeSpeed = 0.5f;

        [Header("Follow Object")]
        public GameObject objectToFollow;
        public Vector3 cameraTarget;

        [Header("Terrain")]
        public bool adaptToTerrainHeight = true;
        public Terrain terrain;

        [Header("Settings")]
        public bool useKeyboardInput = true;
        public bool useMouseInput = true;
        public bool increaseSpeedWhenZoomedOut = true;
        public bool correctZoomingOutRatio = true;
        public bool smoothing = true;

        // private fields.
        private float mousePanMultiplier = 0.1f;
        private float mouseRotationMultiplier = 0.2f;
        private float mouseZoomMultiplier = 5.0f;

        private float smoothingFactor = 0.1f;
        private float goToSpeed = 0.1f;
        private float panSpeed = 10.0f;

        private float currentCameraDistance;
        private Vector3 lastMousePos;
        private Vector3 lastPanSpeed = Vector3.zero;
        private Vector3 goingToCameraTarget = Vector3.zero;
        private bool doingAutoMovement = false;


        // Use this for initialization
        public void Start()
        {
            currentCameraDistance = minZoomDistance + ((maxZoomDistance - minZoomDistance) / 2.0f);
            lastMousePos = Vector3.zero;
        }

        // Update is called once per frame
        public void Update()
        {
            UpdatePanning();
            UpdateRotation();
            UpdateZooming();
            UpdatePosition();
            UpdateAutoMovement();
            lastMousePos = Input.mousePosition;
        }

        public void GoTo(Vector3 position)
        {
            doingAutoMovement = true;
            goingToCameraTarget = position;
            objectToFollow = null;
        }

        public void Follow(GameObject gameObjectToFollow)
        {
            objectToFollow = gameObjectToFollow;
        }

        private void UpdatePanning()
        {
            Vector3 moveVector = new Vector3(0, 0, 0);
            if (useKeyboardInput)
            {
                //! rewrite to adress xyz seperatly
                if (Input.GetKey(KeyCode.A))
                {
                    moveVector.x -= 1;
                }
                if (Input.GetKey(KeyCode.S))
                {
                    moveVector.z -= 1;
                }
                if (Input.GetKey(KeyCode.D))
                {
                    moveVector.x += 1;
                }
                if (Input.GetKey(KeyCode.W))
                {
                    moveVector.z += 1;
                }
            }
            if (allowScreenEdgeMovement)
            {
                if (Input.mousePosition.x < screenEdgeSize)
                {
                    moveVector.x -= screenEdgeSpeed;
                }
                else if (Input.mousePosition.x > Screen.width - screenEdgeSize)
                {
                    moveVector.x += screenEdgeSpeed;
                }
                if (Input.mousePosition.y < screenEdgeSize)
                {
                    moveVector.z -= screenEdgeSpeed;
                }
                else if (Input.mousePosition.y > Screen.height - screenEdgeSize)
                {
                    moveVector.z += screenEdgeSpeed;
                }
            }

            if (useMouseInput)
            {
                if (Input.GetMouseButton(2) && Input.GetKey(KeyCode.LeftShift))
                {
                    Vector3 deltaMousePos = (Input.mousePosition - lastMousePos);
                    moveVector += new Vector3(-deltaMousePos.x, 0, -deltaMousePos.y) * mousePanMultiplier;
                }
            }

            if (moveVector != Vector3.zero)
            {
                objectToFollow = null;
                doingAutoMovement = false;
            }

            var effectivePanSpeed = moveVector;
            if (smoothing)
            {
                effectivePanSpeed = Vector3.Lerp(lastPanSpeed, moveVector, smoothingFactor);
                lastPanSpeed = effectivePanSpeed;
            }

            var oldXRotation = transform.localEulerAngles.x;

            // Set the local X rotation to 0;
            SetLocalEulerAngles(transform, 0.0f);

            float panMultiplier = increaseSpeedWhenZoomedOut ? (Mathf.Sqrt(currentCameraDistance)) : 1.0f;
            cameraTarget = cameraTarget + transform.TransformDirection(effectivePanSpeed) * panSpeed * panMultiplier * Time.deltaTime;

            // Set the old x rotation.
            SetLocalEulerAngles(transform, oldXRotation);
        }

        private void UpdateRotation()
        {
            float deltaAngleH = 0.0f;
            float deltaAngleV = 0.0f;

            if (useKeyboardInput)
            {
                if (Input.GetKey(KeyCode.Q))
                {
                    deltaAngleH = 1.0f;
                }
                if (Input.GetKey(KeyCode.E))
                {
                    deltaAngleH = -1.0f;
                }
            }

            if (useMouseInput)
            {
                if (Input.GetMouseButton(2) && !Input.GetKey(KeyCode.LeftShift))
                {
                    var deltaMousePos = (Input.mousePosition - lastMousePos);
                    deltaAngleH += deltaMousePos.x * mouseRotationMultiplier;
                    deltaAngleV -= deltaMousePos.y * mouseRotationMultiplier;
                }
            }

            SetLocalEulerAngles(transform,
                Mathf.Min(80.0f, Mathf.Max(5.0f, transform.localEulerAngles.x + deltaAngleV * Time.deltaTime * rotationSpeed)),
                transform.localEulerAngles.y + deltaAngleH * Time.deltaTime * rotationSpeed
            );
        }

        private void UpdateZooming()
        {
            float deltaZoom = 0.0f;
            if (useKeyboardInput)
            {
                if (Input.GetKey(KeyCode.F))
                {
                    deltaZoom = 1.0f;
                }
                if (Input.GetKey(KeyCode.R))
                {
                    deltaZoom = -1.0f;
                }
            }
            if (useMouseInput)
            {
                var scroll = Input.GetAxis("Mouse ScrollWheel");
                deltaZoom -= scroll * mouseZoomMultiplier;
            }
            var zoomedOutRatio = correctZoomingOutRatio ? (currentCameraDistance - minZoomDistance) / (maxZoomDistance - minZoomDistance) : 0.0f;
            currentCameraDistance = Mathf.Max(minZoomDistance, Mathf.Min(maxZoomDistance, currentCameraDistance + deltaZoom * Time.deltaTime * zoomSpeed * (zoomedOutRatio * 2.0f + 1.0f)));
        }

        private void UpdatePosition()
        {
            if (objectToFollow != null)
            {
                cameraTarget = Vector3.Lerp(cameraTarget, objectToFollow.transform.position, goToSpeed);
            }

            transform.position = cameraTarget;
            transform.Translate(Vector3.back * currentCameraDistance);

            if (adaptToTerrainHeight && terrain != null)
            {
                SetPosition(transform,
                    null,
                    Mathf.Max(terrain.SampleHeight(transform.position) + terrain.transform.position.y + 10.0f, transform.position.y)
                );
            }
        }

        private void UpdateAutoMovement()
        {
            if (doingAutoMovement)
            {
                cameraTarget = Vector3.Lerp(cameraTarget, goingToCameraTarget, goToSpeed);
                if (Vector3.Distance(goingToCameraTarget, cameraTarget) < 1.0f)
                {
                    doingAutoMovement = false;
                }
            }
        }

        void SetLocalEulerAngles(Transform transform, float? x = null, float? y = null, float? z = null)
        {
            var vector = new Vector3();
            if (x != null) { vector.x = x.Value; } else { vector.x = transform.localEulerAngles.x; }
            if (y != null) { vector.y = y.Value; } else { vector.y = transform.localEulerAngles.y; }
            if (z != null) { vector.z = z.Value; } else { vector.z = transform.localEulerAngles.z; }
            transform.localEulerAngles = vector;
        }

        void SetPosition(Transform transform, float? x = null, float? y = null, float? z = null)
        {
            var vector = new Vector3();
            if (x != null) { vector.x = x.Value; } else { vector.x = transform.position.x; }
            if (y != null) { vector.y = y.Value; } else { vector.y = transform.position.y; }
            if (z != null) { vector.z = z.Value; } else { vector.z = transform.position.z; }
            transform.position = vector;
        }
    }
}