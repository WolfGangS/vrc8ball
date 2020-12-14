
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class ht8b_positioner : UdonSharpBehaviour {

[SerializeField] ht8b main;

void OnPickupUseDown()
{
	main.PosFinalize();
}

}