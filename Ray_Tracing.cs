using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System;

/// <summary>
/// Represents the data for a single ray trace, including its origin, direction, and hit information.
/// This struct is immutable (readonly) for thread safety and performance.
/// </summary>
[System.Serializable]
public readonly struct RayData
{
    public readonly Vector3 origin;      // Starting point of the ray
    public readonly Vector3 direction;    // Direction of the ray
    public readonly Vector3 hitPoint;     // Point where the ray hits a surface
    public readonly Vector3 hitNormal;    // Normal vector at the hit point
    public readonly float totalDistance;  // Cumulative distance traveled by the ray
    public readonly float rayDistance;    // Distance of this specific ray segment
    public readonly char hitTag;          // First character of the hit object's tag

    public RayData(Vector3 origin, Vector3 direction, RaycastHit hit, float totalDistance)
    {
        this.origin = origin;
        this.direction = direction;
        this.hitPoint = hit.point;
        this.hitNormal = hit.normal;
        this.rayDistance = hit.distance;
        this.totalDistance = totalDistance + this.rayDistance;
        this.hitTag = hit.collider.tag[0];
    }
}

/// <summary>
/// Implements ray tracing functionality for simulating signal propagation in a 3D environment.
/// Handles reflection, refraction through foliage, and records path data to CSV files.
/// </summary>
public class RayTracing : MonoBehaviour
{
    [Header("Ray Tracing Settings")]
    [SerializeField] private float maxRayDistance = 500f;           // Maximum distance a ray can travel
    [SerializeField] private int numberOfReflections = 2;           // Maximum number of reflections per ray
    [SerializeField] private int numberOfRays = 1000;              // Total number of rays to simulate
    [SerializeField] private float foliageOffset = 0.01f;          // Small offset to prevent ray getting stuck in foliage collider
    [SerializeField] private LayerMask reflectionLayer;            // Layers that rays can interact with
    [SerializeField] private float foliageRelativePermitivity = 4f;// Relative permittivity of foliage material

    [Header("File Settings")]
    [SerializeField] private string baseFolderPath = "C:\\Users\\vamsh\\Desktop\\cvsfilesForBTP";

    private const int INITIAL_RAY_LIST_CAPACITY = 5;
    private readonly List<RayData> rayDataList = new List<RayData>(INITIAL_RAY_LIST_CAPACITY);
    private string csvFilePath;
    private Vector3 transmitterPosition;
    private int hitCount;                 // Counter for successful ray hits on receiver
    private StringBuilder csvBuilder;      // Reusable string builder for CSV operations

    /// <summary>
    /// Initializes the StringBuilder on component creation
    /// </summary>
    private void Awake()
    {
        csvBuilder = new StringBuilder(1024);
    }

    /// <summary>
    /// Starts the ray tracing simulation when the scene loads
    /// </summary>
    private void Start()
    {
        if (!InitializeSystem()) return;
        PerformRayTracing();
        Debug.Log($"Ray tracing completed. Hit count: {hitCount}");
    }

    /// <summary>
    /// Sets up the necessary components for ray tracing
    /// </summary>
    private bool InitializeSystem()
    {
        hitCount = 0;
        if (!CreateOutputDirectory()) return false;
        if (!SetTransmitterPosition()) return false;
        InitializeCSVFile();
        return true;
    }

    /// <summary>
    /// Creates a timestamped output directory for storing results
    /// </summary>
    private bool CreateOutputDirectory()
    {
        try
        {
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string currentFolderPath = Path.Combine(baseFolderPath, $"Run_{timestamp}");
            Directory.CreateDirectory(currentFolderPath);
            csvFilePath = Path.Combine(currentFolderPath, "ray_data.csv");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to create output directory: {e.Message}");
            return false;
        }
    }

    /// <summary>
    /// Locates and sets the transmitter position in the scene
    /// </summary>
    private bool SetTransmitterPosition()
    {
        GameObject transmitter = GameObject.FindWithTag("transmitter");
        if (transmitter == null)
        {
            Debug.LogError("No game object with tag 'transmitter' found!");
            return false;
        }
        transmitterPosition = transmitter.transform.position;
        return true;
    }

    /// <summary>
    /// Main method that performs the ray tracing simulation
    /// </summary>
    private void PerformRayTracing()
    {
        Vector3[] directions = PrecomputeRandomDirections();

        for (int i = 0; i < numberOfRays; i++)
        {
            TraceRay(transmitterPosition, directions[i]);
        }
    }

    /// <summary>
    /// Precomputes random directions for all rays to improve performance
    /// </summary>
    private Vector3[] PrecomputeRandomDirections()
    {
        Vector3[] directions = new Vector3[numberOfRays];
        for (int i = 0; i < numberOfRays; i++)
        {
            directions[i] = UnityEngine.Random.onUnitSphere;
        }
        return directions;
    }

    /// <summary>
    /// Traces a single ray through the environment, handling reflections and refractions
    /// </summary>
    private void TraceRay(Vector3 initialOrigin, Vector3 initialDirection)
    {
        rayDataList.Clear();
        Vector3 currentOrigin = initialOrigin;
        Vector3 currentDirection = initialDirection;
        float totalDistance = 0f;
        bool inFoliage = false;
        RaycastHit hit;

        for (int reflection = 0; reflection <= numberOfReflections; reflection++)
        {
            if (!Physics.Raycast(currentOrigin, currentDirection, out hit, maxRayDistance, reflectionLayer))
                break;

            RayData currentRayData = new RayData(currentOrigin, currentDirection, hit, totalDistance);
            rayDataList.Add(currentRayData);

            if (ProcessHit(ref currentOrigin, ref currentDirection, hit, ref totalDistance, ref inFoliage, ref reflection))
                return;
        }
    }

    /// <summary>
    /// Processes a ray hit and determines the next ray behavior based on the hit object's tag
    /// </summary>
    private bool ProcessHit(ref Vector3 origin, ref Vector3 direction, RaycastHit hit, ref float totalDistance, ref bool inFoliage, ref int reflection)
    {
        switch (hit.collider.tag)
        {
            case "receiver":      // Ray hit the receiver
                if (!inFoliage)
                {
                    hitCount++;
                    SaveRayDataToCSV();
                }
                return true;

            case "ground" when inFoliage:  // Ray hit ground while in foliage
                return true;

            case "foliage":       // Ray hit foliage - calculate refraction
                reflection--;
                float Eri = inFoliage ? foliageRelativePermitivity : 1;
                float Err = inFoliage ? 1 : foliageRelativePermitivity;
                direction = CalculateRefractionDirection(direction, hit.normal, Eri, Err);
                origin = hit.point + direction * foliageOffset;
                inFoliage = !inFoliage;
                totalDistance += hit.distance;
                return false;

            default:              // Ray hit a regular surface - calculate reflection
                origin = hit.point;
                totalDistance += hit.distance;
                direction = Vector3.Reflect(direction, hit.normal);
                return false;
        }
    }

    /// <summary>
    /// Calculates the direction of a refracted ray using Snell's law
    /// </summary>
    private static Vector3 CalculateRefractionDirection(Vector3 incidentDirection, Vector3 normal, float Eri, float Err)
    {
        float relativeRefractionIndex = Mathf.Sqrt(Eri / Err);
        float cosIncident = -Vector3.Dot(normal, incidentDirection);
        float sinRefractedSquared = relativeRefractionIndex * relativeRefractionIndex * (1 - cosIncident * cosIncident);

        // Total internal reflection case
        if (sinRefractedSquared > 1.0f)
            return Vector3.Reflect(incidentDirection, normal);

        float cosRefracted = Mathf.Sqrt(1.0f - sinRefractedSquared);
        return (relativeRefractionIndex * incidentDirection +
                (relativeRefractionIndex * cosIncident - cosRefracted) * normal).normalized;
    }

    /// <summary>
    /// Creates and initializes the CSV file with column headers
    /// </summary>
    private void InitializeCSVFile()
    {
        try
        {
            File.WriteAllText(csvFilePath,
                "origin_x,origin_y,origin_z,direction_x,direction_y,direction_z," +
                "hitPoint_x,hitPoint_y,hitPoint_z,hitNormal_x,hitNormal_y,hitNormal_z," +
                "totalDistance,rayDistance,hitTag\n");
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to initialize CSV file: {e.Message}");
        }
    }

    /// <summary>
    /// Saves the current ray trace data to the CSV file
    /// Includes visualization in Unity Editor for debugging
    /// </summary>
    private void SaveRayDataToCSV()
    {
        try
        {
            csvBuilder.Clear();
            csvBuilder.AppendLine($"--- Ray Trace {hitCount} ---");

            foreach (RayData rd in rayDataList)
            {
#if UNITY_EDITOR
                Debug.DrawRay(rd.origin, rd.hitPoint - rd.origin, Color.blue, 1000f);
#endif

                csvBuilder.AppendFormat(
                    "{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14}\n",
                    rd.origin.x, rd.origin.y, rd.origin.z,
                    rd.direction.x, rd.direction.y, rd.direction.z,
                    rd.hitPoint.x, rd.hitPoint.y, rd.hitPoint.z,
                    rd.hitNormal.x, rd.hitNormal.y, rd.hitNormal.z,
                    rd.totalDistance, rd.rayDistance,
                    rd.hitTag);
            }

            csvBuilder.AppendLine();
            File.AppendAllText(csvFilePath, csvBuilder.ToString());
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to save ray data to CSV: {e.Message}");
        }
    }
}
