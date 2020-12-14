
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class ht8b_startgame : UdonSharpBehaviour {
[SerializeField] ht8b main;

private void OnTriggerEnter(Collider other)
{
	main.NewGame();
}
}
