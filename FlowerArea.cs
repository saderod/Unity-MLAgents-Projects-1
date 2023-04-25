using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FlowerArea : MonoBehaviour
{
    // The diameter of the area where the agent and flowers can be
    // used for observing relative distance from agent and flower
    public const float AreaDiameter = 20f;

    // the list of all flower plants in this flower area (flower plants have multiple flowers)
    private List<GameObject> flowerPlants;

    // A lookup dictionary for looking up a flower from a nectar collider
    private Dictionary<Collider, Flower> nectarFlowerDictionary; 

    /// <summary>
    /// List of all flowers in the flower area
    /// </summary>
    public List<Flower> Flowers { get; private set; }

    /// <summary>
    /// Reset the flowers and flower plant
    /// </summary>
    public void ResetFlowers()
    {
        // Rotate each flower plant around the Y axis and subtly around X and Z
        foreach (GameObject flowerPlant in flowerPlants)
        {
            float xRotation = UnityEngine.Random.Range(-5f, 5f);
            float yRotation = UnityEngine.Random.Range(180f, 180f);
            float zRotation = UnityEngine.Random.Range(-5f, 5f);

            flowerPlant.transform.localRotation = Quaternion.Euler(xRotation, yRotation, zRotation);
        }

        foreach (Flower flower in Flowers)
        {
            flower.ResetFlower();
        }
    }

    /// <summary>
    /// Gets the <see cref="Flower"/> that a nectar collider belongs to
    /// </summary>
    /// <param name="collider">The nectar collider</param>
    /// <returns>The matching flower</returns>
    public Flower GetFlowerFromNectar(Collider collider)
    {
        return nectarFlowerDictionary[collider];
    }

    private void Awake()
    {
        flowerPlants = new List<GameObject>();
        nectarFlowerDictionary = new Dictionary<Collider, Flower>();
        Flowers = new List<Flower>();
    }

    /// <summary>
    /// called when the game starts
    /// </summary>
    private void Start()
    {
        // find all flowes that are children of this game obj/tranform
        FindChildFlowers(transform);
    }

    /// <summary>
    /// recursively finds all the flowers and plants that are childen of a parent transform
    /// </summary>
    /// <param name="parent">The parent of the children to check</param>
    private void FindChildFlowers(Transform parent)
    {
        for (int i =0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);

            if (child.CompareTag("flower_plant"))
            {
                // Found a flower plant, add it to flowerplants list
                flowerPlants.Add(child.gameObject);

                FindChildFlowers(child);
            }
            else
            {
                // Not a flower plant, look for a flower component
                Flower flower = child.GetComponent<Flower>();
                if (flower != null)
                {
                    Flowers.Add(flower);

                    nectarFlowerDictionary.Add(flower.nectarCollider, flower);


                }
                else
                {
                    FindChildFlowers(child);
                }
            }
        }
    }
    
}
