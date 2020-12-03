
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class ht8b_takeowner : UdonSharpBehaviour
{
	[SerializeField]
	GameObject target;

	void Interact()
	{
		Networking.SetOwner( Networking.LocalPlayer, target );
	}
}
