
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class ht8b_collapse : UdonSharpBehaviour
{

[SerializeField] GameObject[] arrObjParts;
[SerializeField] GameObject target;

[SerializeField]	float speedZ = 30.0f;
[SerializeField]	float[] stateZ = new float[2];
						float currentZ;
						float targetZ;
						uint	state = 0x1U;

const float epsilon = 0.001f;

void Interact()
{
	if( (state & 0x2U) == 0 )
	{
		targetZ = stateZ[ state ];
		state ^= 0x3U;
	}
}

void Update()
{
	if( (state & 0x2U) > 0 )
	{
		if( Mathf.Abs( currentZ - targetZ ) < epsilon )
		{
			state = state & 0x1U;
		}

		currentZ = Mathf.Lerp( currentZ, targetZ, Time.deltaTime * speedZ );

		Vector3 lpos = new Vector3( 0,0, currentZ );

		arrObjParts[0].transform.localPosition = lpos;
		arrObjParts[1].transform.localPosition = lpos;
	}
}

private void Start()
{
	currentZ = stateZ[0];
}

}
