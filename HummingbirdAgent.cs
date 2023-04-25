using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;

public class HummingbirdAgent : Agent
{
    [Tooltip("Force to apply when moving")]
    public float moveForce = 2f;

    [Tooltip("Sped to pitch up or down")]
    public float pitchSpeed = 100f;

    [Tooltip("Speed to rotate aroun the up axis")]
    public float yawSpeed = 100f;

    [Tooltip("Transform at the tip of the beak")]
    public Transform beakTip;

    [Tooltip("The agent's camera")]
    public Camera agentCamera;

    [Tooltip("whether this is training mode or gameplay")]
    public bool trainingMode;

    // The rigid body of the agent
    new private Rigidbody rigidbody;

    // The flower area the agent is in
    new private FlowerArea flowerArea;

    // The nearest flower to the agent
    private Flower nearestFlower;

    // Allows for smoother pitch chnages
    private float smoothPitchChange = 0f;

    // Allows for smoother yaw changes
    private float smoothYawChange = 0f;

    // Max angle that the bird can pitch up and down
    private const float MaxPitchAngle = 80f;

    // Max distance from beak tip to accept nectar collision
    private const float BeakTipRadius = 0.008f;

    // Whether the agent is frozen (intentionally flying)
    private bool frozen = false;

    /// <summary>
    /// The amount of nectar the agent has obtained this epsiode
    /// </summary>
    public float NectarObtained { get; private set; }


    /// <summary>
    /// Initialize the agent 
    /// </summary>
    public override void Initialize()
    {
        rigidbody = GetComponent<Rigidbody>();
        flowerArea = GetComponentInParent<FlowerArea>();

        // if not training mode, no max step, play forever
        if (!trainingMode) MaxStep = 0;
    }

    public override void OnEpisodeBegin()
    {
        if (trainingMode)
        {
            // Only reset flowers in training when there is a one agent per area
            flowerArea.ResetFlowers();
        }

        // Reset nectar obtained
        NectarObtained = 0f;

        // Zero out velocity so that moving stops before new episode starts
        rigidbody.velocity = Vector3.zero;
        rigidbody.angularVelocity = Vector3.zero;

        // Default to spawning in front of a flower
        bool inFrontOfFlower = true;
        if (trainingMode)
        {
            // Spawn in front of flower 50% of the time during training
            inFrontOfFlower = UnityEngine.Random.value > .5f;
        }

        // Move the agent to a new random position
        MoveToSafeRandomPosition(inFrontOfFlower);

        // Recalc the nearest flower now that the agent has moved
        UpdateNearestFlower();

    }


    /// <summary>
    /// Called when an action is received from either the player input or the neural network
    /// 
    /// vectorAction[i] represents:
    /// Index 0: movement in x (+1 = right, -1 = left)
    /// Index 1-4 follow th same idea for y, z, pitch angle and yaw angle
    /// </summary>
    /// <param name="vectorAction"></param>
    public void OnActionReceived(float[] vectorAction)
    {
        // dont take actions if frozen
        if (frozen) return;

        // Calc movement vector
        Vector3 move = new Vector3(vectorAction[0], vectorAction[1], vectorAction[2]);

        // Add force in the direction of the move vector
        rigidbody.AddForce(move * moveForce);

        // get current rotation
        Vector3 rotationVector = transform.rotation.eulerAngles;

        // calc pitch and yaw rotation
        float pitchChange = vectorAction[3];
        float yawChange = vectorAction[4];

        // Calc smooth rotation changes
        smoothPitchChange = Mathf.MoveTowards(smoothPitchChange, pitchChange, 2f * Time.fixedDeltaTime);
        smoothYawChange = Mathf.MoveTowards(smoothYawChange, yawChange, 2f * Time.fixedDeltaTime);

        // Calc new pitch and yaw based on smoothed values
        // clamp pitch to avoid flipping upside down
        float pitch = rotationVector.x + smoothPitchChange * Time.fixedDeltaTime * pitchSpeed;
        if (pitch > 180f) pitch -= 360f;
        pitch = Mathf.Clamp(pitch, -MaxPitchAngle, MaxPitchAngle);

        float yaw = rotationVector.y + smoothYawChange * Time.fixedDeltaTime * yawSpeed;

        // Apply the new rotation
        transform.rotation = Quaternion.Euler(pitch, yaw, 0f);

    }

    /// <summary>
    /// collect vector observations from the environment
    /// </summary>
    /// <param name="sensor">the vector sensor</param>
    public override void CollectObservations(VectorSensor sensor)
    {
        // If nearestFlower is null, observe an empty array and return early
        if (nearestFlower == null)
        {
            sensor.AddObservation(new float[10]);
            return;
        }

        // observe agent's location (4 observations)
        sensor.AddObservation(transform.localRotation.normalized);

        // geta vector from the beak tip to the nearest flower
        Vector3 toFlower = nearestFlower.FlowerCenterPosition - beakTip.position;

        // Observe a normalized vector point to the nearest flower (3 observations)
        sensor.AddObservation(toFlower.normalized);

        // Observe a dot product that indicates whether the beak tip is in front of the flower (1 observe)
        // (+1 means that the beak tip is directly in front of the flower, -1 menas directly behind
        sensor.AddObservation(Vector3.Dot(toFlower.normalized, -nearestFlower.FlowerUpVector.normalized));

        // Observe a dot product that indicates if the beak is pointing toward the flower (1 observe)
        // (+1 means the beak is pointing at the flower, -1 means directly away)
        sensor.AddObservation(Vector3.Dot(beakTip.forward.normalized, -nearestFlower.FlowerUpVector.normalized));

        // Observe the relative distance from the beak tip to the flower(1 observe)
        sensor.AddObservation(toFlower.magnitude / FlowerArea.AreaDiameter);

        // 10 observations


    }

    /// <summary>
    /// when behavior type is set to "Heuristic Only" on the agent's Behavior Parameters
    /// this function will be called. Its return values will be fed into
    /// <see cref="OnActionReceived(float[])"/> instead of using the neural network
    /// </summary>
    /// <param name="actionsOut">an output action array</param>
    public void Heuristic(float[] actionsOut)
    {
        // create placeholders for all movement/turning
        Vector3 forward = Vector3.zero;
        Vector3 left = Vector3.zero;
        Vector3 up = Vector3.zero;
        float pitch = 0f;
        float yaw = 0f;

        //Convert keyboard inputs into moving and turning; all vlaues between 1 and -1

        // Forward / Backward
        if (Input.GetKey(KeyCode.W)) forward = transform.forward;
        else if (Input.GetKey(KeyCode.S)) forward = -transform.forward;

        // Left / Right
        if (Input.GetKey(KeyCode.A)) left = -transform.right;
        else if (Input.GetKey(KeyCode.D)) left = transform.right;

        // up / down
        if (Input.GetKey(KeyCode.E)) up = transform.up;
        else if (Input.GetKey(KeyCode.C)) up = -transform.up;

        // pitch up / down
        if (Input.GetKey(KeyCode.UpArrow)) pitch = 1f;
        else if (Input.GetKey(KeyCode.DownArrow)) pitch = -1f;

        // turn left / right
        if (Input.GetKey(KeyCode.LeftArrow)) yaw = -1f;
        else if (Input.GetKey(KeyCode.RightArrow)) yaw = 1f;

        // Combine the movement vectors and normalize
        Vector3 combined = (forward + left + up).normalized;

        // Add the 3 movement values, pitch and yaw to the actionsOut array
        actionsOut[0] = combined.x;
        actionsOut[1] = combined.y;
        actionsOut[2] = combined.z;
        actionsOut[3] = pitch;
        actionsOut[4] = yaw;

    }

    /// <summary>
    /// Prevent the agent from moving and tkaing actions
    /// </summary>
    public void FreezeAgent()
    {
        Debug.Assert(trainingMode == false, "Freeze/Unfreeze not supported in traning");
        frozen = true;
        rigidbody.Sleep();
    }

    /// <summary>
    /// Resume agent moving actions
    /// </summary>
    public void UnFreezeAgent()
    {
        Debug.Assert(trainingMode == false, "Freeze/Unfreeze not supported in traning");
        frozen = false;
        rigidbody.WakeUp();
    }


    /// <summary>
    /// Move the agent to a safe random place (not in a bush)
    /// if in front of flower then [oint the peak at flower
    /// </summary>
    /// <param name="inFrontOfFlower">Whether to choose a spot in front of the flower</param>
    private void MoveToSafeRandomPosition(bool inFrontOfFlower)
    {
        bool safePositionFound = false;
        int attemptsRemaining = 100; // prevent infinite loop
        Vector3 potentialPosition = Vector3.zero;
        Quaternion potentialRotation = new Quaternion();

        // Loop until a safe position found or we run out of attempts
        while(!safePositionFound && attemptsRemaining > 0)
        {
            attemptsRemaining--;

            if (inFrontOfFlower)
            {
                // Pick a random flower
                Flower randomFlower = flowerArea.Flowers[UnityEngine.Random.Range(0, flowerArea.Flowers.Count)];

                // Position 10 to 20 cm in front of the flower
                float distanceFromFlower = UnityEngine.Random.Range(.1f, .2f);
                potentialPosition = randomFlower.transform.position + randomFlower.FlowerUpVector * distanceFromFlower;

                // Point beak at flower (bird's head center of transform)
                Vector3 toFlower = randomFlower.FlowerCenterPosition - potentialPosition;

                potentialRotation = Quaternion.LookRotation(toFlower, Vector3.up);
            }
            else
            {
                // Pick a random height from the ground
                float height = UnityEngine.Random.Range(1.2f, 2.5f);

                // Pick a random radius from the center of the area
                float radius = UnityEngine.Random.Range(2f, 7f);

                // Pick a random direction rotated around the y axis
                Quaternion direction = Quaternion.Euler(0f, UnityEngine.Random.Range(-180f, 180f), 0f);

                // Combine height, radius and directionto pick a potential position
                potentialPosition = flowerArea.transform.position + Vector3.up * height + direction * Vector3.forward * radius;


                // Choose and set random starting pitch and yaw
                float pitch = UnityEngine.Random.Range(-60f, 60f);
                float yaw = UnityEngine.Random.Range(-180f, 180f);
                potentialRotation = Quaternion.Euler(pitch, yaw, 0f); 
            }

            // check to see if the agent will collide with anything
            Collider[] colliders = Physics.OverlapSphere(potentialPosition, 0.05f);

            // Safe position has been found if no colliders are overlapped
            safePositionFound = colliders.Length == 0;
        }

        Debug.Assert(safePositionFound, "Could not find safe position to spawn");

        // Set the position and rotation
        transform.position = potentialPosition;
        transform.rotation = potentialRotation;
    }

    private void UpdateNearestFlower()
    {
        foreach (Flower flower in flowerArea.Flowers)
        {
            if (nearestFlower == null && flower.HasNectar)
            {
                // no current nearest flower and this flower has nectar, so set to this flower
                nearestFlower = flower;
            }
            else if (flower.HasNectar)
            {
                // Calc distance to this flower and dist to ther current nearest flower
                float distanceToFlower = Vector3.Distance(flower.transform.position, beakTip.position);
                float distanceToCurrentNearestFlower = Vector3.Distance(nearestFlower.transform.position, beakTip.position);

                // If current nearest flower is empty or this flower is closer, update the nearest flower
                if (!nearestFlower.HasNectar || distanceToFlower < distanceToCurrentNearestFlower) 
                {
                    nearestFlower = flower;
                }
            }
        }
    }

    /// <summary>
    /// Called when the agent's collider enters a trigger collider
    /// </summary>
    /// <param name="other">trigger collider</param>
    private void OnTriggerEnter(Collider other)
    {
        TriggerEnterOrStay(other);
    }

    // <summary>
    /// Called when the agent's collider enters a trigger collider
    /// </summary>
    /// <param name="other">trigger collider</param>
    private void OnTriggerStay(Collider other)
    {
        TriggerEnterOrStay(other);
    }

    /// <summary>
    /// handles when the agent's collider stays or enters into a trigger collider
    /// </summary>
    /// <param name="collider">trigger collider</param>
    private void TriggerEnterOrStay(Collider collider)
    {
        // check if agent is colliding with nectar
        if (collider.CompareTag("nectar"))
        {
            Vector3 closestPointToBeakTip = collider.ClosestPoint(beakTip.position);

            //Check if the closest collision point is close to the beak tip
            // Note: a collision with anythin but the beak tip should not count
            if (Vector3.Distance(beakTip.position, closestPointToBeakTip) < BeakTipRadius)
            {
                // Look up the flower for this nectar collider
                Flower flower = flowerArea.GetFlowerFromNectar(collider);

                // Attempt to take 0.01 nectar
                // this happens every 0.02 seconds or 50x per second
                float nectarRecevied = flower.Feed(0.01f);

                // Keep track of nectar obtained
                NectarObtained += nectarRecevied;

                if (trainingMode)
                {
                    // Calc reward for getting nectar
                    float bonus = 0.2f * Mathf.Clamp01(Vector3.Dot(transform.forward.normalized, -nearestFlower.FlowerUpVector.normalized));
                    AddReward(0.01f + bonus);
                }

                // if flower is empty, update the nearest flower
                if (!flower.HasNectar)
                {
                    UpdateNearestFlower();
                }
            }
        }
    }
    
    /// <summary>
    /// called when agent hits something solid
    /// </summary>
    /// <param name="collision">collisio info</param>
    private void OnCollisionEnter(Collision collision)
    {
        if (trainingMode && collision.collider.CompareTag("boundary"))
        {
            // Collided with area boundary, give a negative reward
            AddReward(-0.5f);
        }
    }

    /// <summary>
    /// called every frame
    /// </summary>
    private void Update()
    {
        // draw a line from the beak tip to the nearest flower
        if (nearestFlower != null)
            Debug.DrawLine(beakTip.position, nearestFlower.FlowerCenterPosition, Color.green);
    }

    /// <summary>
    /// called every 0.02 seconds
    /// </summary>
    private void FixedUpdate()
    {
        // Avoids scenario where nearest flower nectar is stolen by opp an not updated
        if (nearestFlower != null && !nearestFlower.HasNectar)
        {
            UpdateNearestFlower();
        }

    }

}
