
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;

public class ht8b_tester : UdonSharpBehaviour
{
	[SerializeField] ht8b main;

	[SerializeField] bool ltest;

	void Interact()
	{
		main.SendDebugImpulse();
	}

	private void Update()
	{
		if( ltest )
		{
			Interact();
			ltest = false;
		}
	}
}
