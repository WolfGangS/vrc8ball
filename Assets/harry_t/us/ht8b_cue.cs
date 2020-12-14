using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class ht8b_cue : UdonSharpBehaviour
{
	[SerializeField]
	public GameObject objTarget;
	public ht8b_otherhand objTargetController;

	[SerializeField]
	public GameObject objCue;

	[SerializeField]
	GameObject fix_trigger;

	[SerializeField]
	public ht8b gameController;

	[SerializeField]
	public GameObject objTip;

	[SerializeField]
	public bool forceTurn = false;

	// ( Experimental ) Allow player ownership autoswitching routine 
	public bool bAllowAutoSwitch = true;
	public int playerID = 0;
	
	Vector3 lag_objTarget;
	Vector3 lag_objBase;

	Vector3 vBase;
	Vector3 vLineNorm;
	Vector3 targetOriginalDelta;

	Vector3 vSnOff;
	float vSnDet;

	bool bArmed = false;
	bool bHolding = false;

	private void OnPickupUseDown()
	{
		bArmed = true;

		// copy target position in
		vBase = this.transform.position;

		// Set up line normal
		vLineNorm = (objTarget.transform.position - vBase).normalized;

		// It should now be able to impulse ball
		gameController.StartHit();
	}
	private void OnPickupUseUp()
	{
		bArmed = false;

		gameController.EndHit();
	}

	private void OnPickup()
	{
		objTarget.transform.localScale = Vector3.one;

		// Register the cuetip with main game
		// gameController.cuetip = objTip; 

		// Not sure if this is necessary to do both since we pickup this one,
		// but just to be safe
		Networking.SetOwner( Networking.LocalPlayer, this.gameObject );
		Networking.SetOwner( Networking.LocalPlayer, objTarget );
		bHolding = true;
		objTargetController.bOtherHold = true;

		// ** experimental **
		if( bAllowAutoSwitch )
		{
			if( playerID == 0 )
				gameController.AutoTake0();
			else
				gameController.AutoTake1();
		}
	}

	private void OnDrop()
	{
		objTarget.transform.localScale = Vector3.zero;
		bHolding = false;
		objTargetController.bOtherHold = false;
	}

	private void Start()
	{
		targetOriginalDelta = this.transform.InverseTransformPoint( objTarget.transform.position );
		OnDrop();
	}

	void Update()
	{
		// Clamp controllers to play boundaries while we have hold of them
		if( bHolding )
		{
			Vector3 temp = this.transform.position;
			temp.x = Mathf.Clamp( temp.x, -4.0f, 4.0f );
			temp.y = Mathf.Clamp( temp.y, -0.8f, 1.5f );
			temp.z = Mathf.Clamp( temp.z, -3.25f, 3.25f );
			this.transform.position = temp;
			temp = objTarget.transform.position;
			temp.x = Mathf.Clamp( temp.x, -4.0f, 4.0f );
			temp.y = Mathf.Clamp( temp.y, -0.8f, 1.5f );
			temp.z = Mathf.Clamp( temp.z, -3.25f, 3.25f );
			objTarget.transform.position = temp;
		}

		lag_objBase = Vector3.Lerp( lag_objBase, this.transform.position, Time.deltaTime * 16.0f );
		lag_objTarget = Vector3.Lerp( lag_objTarget, objTarget.transform.position, Time.deltaTime * 16.0f );

		if( bArmed )
		{
			vSnOff = lag_objBase - vBase;
			vSnDet = Vector3.Dot( vSnOff, vLineNorm );
			objCue.transform.position = vBase + vLineNorm * vSnDet;
		}
		else
		{
			// put cue at base position
			objCue.transform.position = lag_objBase;
			objCue.transform.LookAt( lag_objTarget );
		}

		// Copy transforms into the floating trigger thing
		fix_trigger.transform.position = objCue.transform.position + new Vector3(0.0f, 10.0f, 0.0f);
		fix_trigger.transform.rotation = objCue.transform.rotation;
	}
}
