
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class ht8b_cue : UdonSharpBehaviour
{
	[SerializeField]
	public GameObject objTarget;

	[SerializeField]
	public GameObject objCue;

	[SerializeField]
	public ht8b gameController;

	[SerializeField]
	public bool forceTurn = false;

	Vector3 lag_objTarget;
	Vector3 lag_objBase;

	Vector3 vBase;
	Vector3 vLineNorm;
	Vector3 targetOriginalDelta;

	Vector3 vSnOff;
	float vSnDet;

	bool bArmed = false;

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
		//objTarget.transform.position = this.transform.TransformPoint( targetOriginalDelta );
		objTarget.SetActive( true );
	}

	private void OnDrop()
	{
		objTarget.SetActive( false );
	}

	private void Start()
	{
		targetOriginalDelta = this.transform.InverseTransformPoint( objTarget.transform.position );
		OnDrop();
	}

	void Update()
	{
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
	}
}
