using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System;

namespace AimlockSystem
{
    [Serializable]
    public struct Vector3Data
    {
        public float x, y, z;
        public Vector3Data(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }
        public Vector3 ToVector3() => new Vector3(x, y, z);
        public static Vector3Data FromVector3(Vector3 v) => new Vector3Data(v.x, v.y, v.z);
    }

    [Serializable]
    public class TargetData
    {
        public Vector3Data position;
        public Vector3Data velocity;
        public float distance;
        public float health;
        public bool isAlive;
        public string playerId;
        public Vector3Data headPosition;
        public Vector3Data chestPosition;
    }

    public class AimlockConfig
    {
        public bool enable = true;
        public float fov = 120f;
        public float smoothness = 0.35f;
        public float smoothnessSwipe = 0.55f;
        public float maxDistance = 300f;
        public float minDistance = 5f;
        public float headOffset = 0.15f;
        public float predictionTime = 0.08f;
        public AimMode aimMode = AimMode.Head;
        public PriorityType priority = PriorityType.Distance;
        public bool autoSwitchTarget = true;
        public float switchDelay = 0.3f;
        public float noTargetWait = 0.5f;
        public bool keepAngleWhenNoTarget = true;
    }

    public enum AimMode
    {
        Head,
        Chest,
        Leg,
        Auto
    }

    public enum PriorityType
    {
        Distance,
        FOV,
        Health,
        Moving
    }

    public class AimlockSystem : MonoBehaviour
    {
        public AimlockConfig config = new AimlockConfig();
        public Transform playerTransform;
        public Camera playerCamera;
        
        private List<TargetData> targetList = new List<TargetData>();
        private TargetData currentTarget = null;
        private float lastSwitchTime = 0f;
        private Vector3 currentAngle;
        private bool isLocking = false;
        private float noTargetTimer = 0f;
        private Vector3 lastValidAngle;
        
        private float[] fovCache = new float[100];
        private float[] distanceCache = new float[100];
        private int cacheIndex = 0;

        void Awake()
        {
            if (playerTransform == null)
                playerTransform = Camera.main.transform;
            
            if (playerCamera == null)
                playerCamera = Camera.main;
            
            currentAngle = playerTransform.eulerAngles;
            lastValidAngle = currentAngle;
        }

        void Update()
        {
            if (!config.enable || playerTransform == null)
                return;

            UpdateTargetList();
            
            if (targetList.Count > 0)
            {
                TargetData bestTarget = SelectBestTarget();
                if (bestTarget != null)
                {
                    ProcessAimlock(bestTarget);
                    isLocking = true;
                    noTargetTimer = 0f;
                }
                else
                {
                    HandleNoTarget();
                }
            }
            else
            {
                HandleNoTarget();
            }
        }

        void UpdateTargetList()
        {
            targetList.Clear();
            
            GameObject[] allPlayers = GameObject.FindGameObjectsWithTag("Player");
            
            foreach (GameObject player in allPlayers)
            {
                if (player == gameObject || player == null)
                    continue;
                
                TargetData data = new TargetData();
                data.position = Vector3Data.FromVector3(player.transform.position);
                data.headPosition = Vector3Data.FromVector3(
                    player.transform.position + Vector3.up * 1.7f
                );
                data.chestPosition = Vector3Data.FromVector3(
                    player.transform.position + Vector3.up * 1.2f
                );
                
                Vector3 direction = data.position.ToVector3() - playerTransform.position;
                data.distance = direction.magnitude;
                
                if (data.distance < config.minDistance || data.distance > config.maxDistance)
                    continue;
                
                Vector3 targetPos = GetTargetPosition(data);
                Vector3 targetDir = targetPos - playerTransform.position;
                float angle = Vector3.Angle(playerTransform.forward, targetDir);
                
                if (angle <= config.fov / 2)
                {
                    data.isAlive = true;
                    data.health = GetPlayerHealth(player);
                    data.playerId = player.GetInstanceID().ToString();
                    
                    targetList.Add(data);
                }
            }
            
            targetList = targetList.OrderBy(t => t.distance).ToList();
        }

        Vector3 GetTargetPosition(TargetData data)
        {
            switch (config.aimMode)
            {
                case AimMode.Head:
                    return data.headPosition.ToVector3() + Vector3.up * config.headOffset;
                case AimMode.Chest:
                    return data.chestPosition.ToVector3();
                case AimMode.Leg:
                    return data.position.ToVector3() + Vector3.down * 0.5f;
                case AimMode.Auto:
                default:
                    if (data.distance < 30f)
                        return data.headPosition.ToVector3();
                    else
                        return data.chestPosition.ToVector3();
            }
        }

        TargetData SelectBestTarget()
        {
            if (targetList.Count == 0)
                return null;

            if (currentTarget != null && config.autoSwitchTarget)
            {
                float switchTimer = Time.time - lastSwitchTime;
                if (switchTimer < config.switchDelay)
                    return currentTarget;
            }

            TargetData bestTarget = null;
            float bestScore = float.MinValue;

            foreach (TargetData target in targetList)
            {
                if (!target.isAlive)
                    continue;

                float score = 0f;

                switch (config.priority)
                {
                    case PriorityType.Distance:
                        score = 1000f / (target.distance + 1f);
                        break;
                    case PriorityType.FOV:
                        Vector3 dir = target.position.ToVector3() - playerTransform.position;
                        float angle = Vector3.Angle(playerTransform.forward, dir);
                        score = 100f / (angle + 1f);
                        break;
                    case PriorityType.Health:
                        score = 100f - target.health;
                        break;
                    case PriorityType.Moving:
                        score = target.velocity.ToVector3().magnitude * 10f;
                        break;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    bestTarget = target;
                }
            }

            if (bestTarget != null && bestTarget != currentTarget)
            {
                lastSwitchTime = Time.time;
            }

            return bestTarget;
        }

        void ProcessAimlock(TargetData target)
        {
            currentTarget = target;
            
            Vector3 targetPos = GetTargetPosition(target);
            Vector3 predictedPos = PredictPosition(target, targetPos);
            
            Vector3 direction = predictedPos - playerTransform.position;
            Quaternion targetRotation = Quaternion.LookRotation(direction);
            Vector3 targetAngle = targetRotation.eulerAngles;
            
            float currentSmoothness = config.smoothness;
            
            if (IsSwipeDetected())
            {
                currentSmoothness = config.smoothnessSwipe;
            }
            
            if (target.distance < 30f)
            {
                currentSmoothness += 0.1f;
            }
            else if (target.distance > 150f)
            {
                currentSmoothness -= 0.05f;
            }
            
            currentSmoothness = Mathf.Clamp(currentSmoothness, 0.05f, 0.95f);
            
            currentAngle = Vector3.Lerp(
                currentAngle,
                targetAngle,
                currentSmoothness
            );
            
            ApplyAngle(currentAngle);
            lastValidAngle = currentAngle;
            
            ApplyBulletCorrection(target);
        }

        Vector3 PredictPosition(TargetData target, Vector3 currentPos)
        {
            if (target.velocity.ToVector3().magnitude < 0.1f)
                return currentPos;
            
            Vector3 predicted = currentPos + target.velocity.ToVector3() * config.predictionTime;
            return predicted;
        }

        void ApplyAngle(Vector3 angle)
        {
            playerTransform.eulerAngles = new Vector3(
                angle.x,
                angle.y,
                0f
            );
        }

        void ApplyBulletCorrection(TargetData target)
        {
            float bulletSpeed = 1500f;
            float gravity = 9.8f;
            float distance = target.distance;
            
            float bulletDrop = 0.5f * gravity * Mathf.Pow(distance / bulletSpeed, 2);
            
            Vector3 correction = Vector3.up * bulletDrop * 0.1f;
            
            Vector3 newPos = playerTransform.position + playerTransform.forward * distance + correction;
            
            RaycastHit hit;
            if (Physics.Raycast(playerTransform.position, playerTransform.forward, out hit, distance))
            {
                if (hit.collider.CompareTag("Player"))
                {
                }
            }
        }

        void HandleNoTarget()
        {
            if (config.keepAngleWhenNoTarget)
            {
                noTargetTimer += Time.deltaTime;
                if (noTargetTimer < config.noTargetWait)
                {
                    ApplyAngle(lastValidAngle);
                }
            }
            
            isLocking = false;
            currentTarget = null;
        }

        bool IsSwipeDetected()
        {
            if (Input.touchCount > 0)
            {
                Touch touch = Input.GetTouch(0);
                float deltaMagnitude = touch.deltaPosition.magnitude;
                return deltaMagnitude > 50f;
            }
            
            float mouseDelta = Input.GetAxis("Mouse X") + Input.GetAxis("Mouse Y");
            return Mathf.Abs(mouseDelta) > 0.5f;
        }

        float GetPlayerHealth(GameObject player)
        {
            PlayerHealth healthComp = player.GetComponent<PlayerHealth>();
            if (healthComp != null)
                return healthComp.currentHealth;
            return 100f;
        }

        void OnDrawGizmosSelected()
        {
            if (playerTransform == null)
                return;
            
            Gizmos.color = Color.green;
            Vector3 forward = playerTransform.forward * 50f;
            Gizmos.DrawRay(playerTransform.position, forward);
            
            Gizmos.color = Color.red;
            for (float angle = -config.fov / 2; angle <= config.fov / 2; angle += 10f)
            {
                Quaternion rot = Quaternion.Euler(0f, angle, 0f);
                Vector3 dir = rot * playerTransform.forward * 50f;
                Gizmos.DrawRay(playerTransform.position, dir);
            }
            
            if (currentTarget != null)
            {
                Gizmos.color = Color.yellow;
                Vector3 targetPos = GetTargetPosition(currentTarget);
                Gizmos.DrawWireSphere(targetPos, 0.5f);
            }
        }
    }

    public class PlayerHealth : MonoBehaviour
    {
        public float currentHealth = 100f;
        public float maxHealth = 100f;
    }
}
using UnityEngine;
using System.Collections.Generic;

public class AimlockManager : MonoBehaviour
{
    public static AimlockManager Instance;
    
    private AimlockSystem aimlock;
    private bool isActive = false;
    
    public bool IsActive => isActive;
    public AimlockSystem System => aimlock;
    
    void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
            Destroy(gameObject);
        
        aimlock = gameObject.AddComponent<AimlockSystem>();
        aimlock.config.enable = false;
    }
    
    public void ToggleAimlock()
    {
        isActive = !isActive;
        aimlock.config.enable = isActive;
        Debug.Log($"Aimlock: {(isActive ? "ON" : "OFF")}");
    }
    
    public void SetFOV(float fov)
    {
        aimlock.config.fov = Mathf.Clamp(fov, 10f, 180f);
    }
    
    public void SetSmoothness(float smooth)
    {
        aimlock.config.smoothness = Mathf.Clamp(smooth, 0.05f, 0.95f);
    }
    
    public void SetAimMode(string mode)
    {
        switch (mode.ToLower())
        {
            case "head":
                aimlock.config.aimMode = AimMode.Head;
                break;
            case "chest":
                aimlock.config.aimMode = AimMode.Chest;
                break;
            case "leg":
                aimlock.config.aimMode = AimMode.Leg;
                break;
            default:
                aimlock.config.aimMode = AimMode.Auto;
                break;
        }
    }
    
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.F1))
        {
            ToggleAimlock();
        }
        
        if (Input.GetKeyDown(KeyCode.F2))
        {
            float fov = aimlock.config.fov;
            SetFOV(fov == 120f ? 60f : 120f);
            Debug.Log($"FOV: {aimlock.config.fov}");
        }
    }
}
