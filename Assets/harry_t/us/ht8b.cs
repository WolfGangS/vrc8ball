
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.Udon;

public class ht8b : UdonSharpBehaviour
{
	[UdonSynced]
	private string netstr; // dumpster fire
	private string netstr_prv;

	[SerializeField]
	GameObject[] balls_render;

	[SerializeField]
	public GameObject cuetip;

	[SerializeField]
	GameObject guideline;

	[SerializeField]
	GameObject devhit;

	[SerializeField]
	Text ltext;

	[SerializeField]
	public bool bAiming = true;

	[SerializeField]
	public bool bArmed = false;

	[SerializeField]
	Vector2 extraGravy;

	// REGION GAME STATE
	// =========================================================================================================================
	int		sn_turn = 0;			// Whos turn is it
	uint		sn_pocketed = 0x00;	// Each bit represents each ball, if it has been pocketed or not
	
	// REGION PHYSICS ENGINE
	// =========================================================================================================================

	bool ballsMoving = false;

	public Vector2[] ball_positions = new Vector2[16];
	Vector2[] ball_originals = new Vector2[16];

	
	public Vector2[] ball_velocities = new Vector2[16];

	Vector3 cue_lpos;
	Vector2 cue_llpos;
	Vector2 cue_vdir;
	float cue_fdir;

	const float MAX_DELTA = 0.1f;
	const float FIXED_TIME_STEP = 0.0125f;
	const float TIME_ALPHA = 50.0f;

	const float TABLE_WIDTH = 1.0668f;
	const float TABLE_HEIGHT = 0.6096f;
	const float BALL_DIAMETRE = 0.06f;
	const float BALL_RSQR = 0.0009f;
	const float POCKET_RADIUS = 0.09f;
	const float K_1OR2 = 0.70710678118f;   // 1 over root 2
	const float K_1OR5 = 0.4472135955f;    // 1 over root 5
	const float POCKET_DEPTH = 0.04f;

	const float FRICTION_EFF = 0.99f;

	void ClampBallVelSemi( int id, Vector2 surface )
	{
		// TODO: improve this method to be a bit more accurate
		if( Vector2.Dot( ball_velocities[id], surface ) < 0.0f )
		{
			ball_velocities[id] = ball_velocities[id].magnitude * surface;
		}
	}

	void PocketBall( int id )
	{
		uint total = 0U;

		// Get total for X positioning
		for( int i = 0; i < 16; i ++ )
		{
			total += (sn_pocketed >> i) & 0x1U;
		}

		// Put balls on the edge of the table for now
		// TODO: propper display
		ball_positions[ id ].x = -TABLE_WIDTH + (float)total * BALL_DIAMETRE;
		ball_positions[ id ].y = TABLE_HEIGHT + BALL_DIAMETRE * 2.0f;

		sn_pocketed ^= 1U << id;
	}

	// TODO: Inline
	bool BallInPlay( int id )
	{
		return ((sn_pocketed >> id) & 0x1U) == 0x00U;
	}

	void BallPockets( int id )
	{
		if( !BallInPlay( id ) )
			return;

		float zy, zx;
		Vector2 A;

		A = ball_positions[ id ];

		// Setup major regions
		zx = A.x > 0.0f ? 1.0f: -1.0f;
		zy = A.y > 0.0f ? 1.0f: -1.0f;

		// Its in a pocket
		if( A.y*zy > TABLE_HEIGHT + POCKET_DEPTH || A.y*zy > A.x*-zx + TABLE_WIDTH+TABLE_HEIGHT + POCKET_DEPTH )
		{
			PocketBall( id );
		}
	}

	// TODO: inline this
	void BallEdges( int id )
	{
		if( !BallInPlay( id ) )
			return;

		float zy, zx, zz, zw, d, k, i, j, l, r;
		Vector2 A, N;

		A = ball_positions[ id ];

		// REGIONS
		/*  
		 *  QUADS:							SUBSECTION:				SUBSECTION:
		 *    zx, zy:							zz:						zw:
		 *																
		 *  o----o----o  +:  1			\_________/				\_________/
		 *  | -+ | ++ |  -: -1		        |	    /		              /  /
		 *  |----+----|					  -  |  +   |		      -     /   |
		 *  | -- | +- |						  |	   |		          /  +  |
		 *  o----o----o						  |      |             /       |
		 * 
		 */

		// Setup major regions
		zx = A.x > 0.0f ? 1.0f: -1.0f;
		zy = A.y > 0.0f ? 1.0f: -1.0f;

		// within pocket regions
		if( (A.y*zy > (TABLE_HEIGHT-POCKET_RADIUS)) && (A.x*zx > (TABLE_WIDTH-POCKET_RADIUS) || A.x*zx < POCKET_RADIUS) )
		{
			// Subregions
			zw = A.y * zy > A.x * zx - TABLE_WIDTH + TABLE_HEIGHT ? 1.0f : -1.0f;

			if (A.x * zx > TABLE_WIDTH * 0.5f)
			{
				zz = 1.0f;
				r = K_1OR2;
			}
			else
			{
				zz = -2.0f;
				r = K_1OR5;
			}

			// Collider line EQ
			d = zx * zy * zz; // Coefficient
			k = (-(TABLE_WIDTH * Mathf.Max(zz, 0.0f)) + POCKET_RADIUS * zw * Mathf.Abs( zz ) + TABLE_HEIGHT) * zy; // Konstant

			// Check if colliding
			l = zw * zy;
			if( A.y * l > (A.x * d + k) * l )
			{
				// Get line normal
				N = new Vector2(zx * zz, -zy) * zw * r;

				// New position
				i = (A.x * d + A.y - k) / (2.0f * d);
				j = i * d + k;

				ball_positions[ id ].Set( i, j );

				// Reflect velocity
				ball_velocities[ id ] = Vector2.Reflect( ball_velocities[ id ], N );

				ClampBallVelSemi( id, N );
			}
		}
		else // L / R edges
		{
			if( A.x * zx > TABLE_WIDTH )
			{
				ball_positions[id].x = TABLE_WIDTH * zx;
				ball_velocities[id] = Vector2.Reflect( ball_velocities[id], Vector2.left * zx );

				ClampBallVelSemi( id, Vector2.left * zx );
			}

			if( A.y * zy > TABLE_HEIGHT )
			{
				ball_positions[id].y = TABLE_HEIGHT * zy;
				ball_velocities[id] = Vector2.Reflect( ball_velocities[id], Vector2.down * zy );

				ClampBallVelSemi( id, Vector2.down * zy );
			}
		}
	}

	void BallSimulate( int id )
	{
		if( !BallInPlay( id ) )
			return;

		// Apply friction
		ball_velocities[ id ] *= FRICTION_EFF;

		// Apply movement
		ball_positions[ id ] += ball_velocities[ id ] * FIXED_TIME_STEP;

		// ball/ball collisions
		for( int i = id+1; i < 16; i++ )
		{
			if( !BallInPlay( id ) )
				continue;

			Vector2 delta = ball_positions[ i ] - ball_positions[ id ];
			float dist = delta.magnitude;

			if( dist < BALL_DIAMETRE )
			{
				Vector2 normal = delta / dist;

				Vector2 velocityDelta = ball_velocities[ id ] - ball_velocities[ i ];

				float dot = Vector2.Dot( velocityDelta, normal );

				if( dot > 0.0f ) 
				{
					Vector2 reflection = normal * dot;
					ball_velocities[id] -= reflection;
					ball_velocities[i] += reflection;
				}
			}
		}

		// ball still moving about
		if( ball_velocities[ id ].x > 0.001f && ball_velocities[ id ].y > 0.001f )
		{
			ballsMoving = true;
		}
	}

	// Ray circle intersection
	// yes, its fixed size circle
	// Output is dispensed into the below variable
	// One intersection point only

	Vector2 RayCircle_output;
	bool RayCircle( Vector2 start, Vector2 dir, Vector2 circle )
	{
		Vector2 nrm = dir.normalized;
		Vector2 h = circle - start;
		float lf = Vector2.Dot( nrm, h );
		float s = BALL_RSQR - Vector2.Dot( h, h ) + lf * lf;

		if( s < 0.0f ) return false;

		s = Mathf.Sqrt( s );

		if( lf < s )
		{
			if( lf + s >= 0 )
			{
				s = -s;
			}
			else
			{
				return false;
			}
		}

		RayCircle_output = start + nrm * (lf-s);
		return true;
	}

	// Closest point on line from pos
	Vector2 LineProject( Vector2 start, Vector2 dir, Vector2 pos )
	{
		return start + dir * Vector2.Dot( pos - start, dir );
	}

	void PhysicsUpdate()
	{
		// Run main simulation / inter-ball collision
		for( int i = 0; i < 16; i ++ )
		{
			BallSimulate( i );
		}

		// Run edge collision
		for( int i = 0; i < 16; i ++ )
		{
			BallEdges( i );
		}

		// Run triggers
		for( int i = 0; i < 16; i ++ )
		{
			BallPockets( i );
		}
	}

	// Events
	public void StartHit()
	{
		// lock aim variables
		bArmed = true;
	}

	public void EndHit()
	{
		bArmed = false;
	}

	float timeLast;
	float accum;

	private void Update()
	{
		// Physics step accumulator routine
		float time = Time.realtimeSinceStartup;
		float timeDelta = time - timeLast;

		if ( timeDelta > MAX_DELTA )
		{
			timeDelta = MAX_DELTA;
		}

		timeLast = time;
		accum += timeDelta;

		while ( accum >= FIXED_TIME_STEP )
		{
			ball_velocities[0] += extraGravy;
			PhysicsUpdate();
			accum -= FIXED_TIME_STEP;
		}

		// float alpha = accum * TIME_ALPHA;

		// Update rendering objects positions
		for( int i = 0; i < 16; i ++ )
		{
			balls_render[i].transform.position = new Vector3( ball_positions[i].x, 0.0f, ball_positions[i].y );
		}

		//Debug.Log( ball_velocities[0].magnitude * FIXED_TIME_STEP );

		
		cue_lpos = cuetip.transform.position;
		Vector2 lpos2 = new Vector2( cue_lpos.x, cue_lpos.z );
		
		if( bArmed )
		{
			float sweep_time_ball = Vector2.Dot( ball_positions[0] - cue_llpos, cue_vdir );

			// Check for potential skips due to low frame rate
			if( sweep_time_ball > 0.0f && sweep_time_ball < (cue_llpos - lpos2).magnitude )
			{
				lpos2 = cue_llpos + cue_vdir * sweep_time_ball;
			}

			// Hit condition is when cuetip is gone inside ball
			if( (lpos2 - ball_positions[0]).sqrMagnitude < BALL_RSQR )
			{
				devhit.SetActive( false );
				guideline.SetActive( false );

				// Compute velocity delta
				float vel = (lpos2 - cue_llpos).magnitude * 10.0f;

				// weeeeeeee
				ball_velocities[0] = cue_vdir * Mathf.Min( vel, 1.0f ) * 14.0f;

				bArmed = false;

				// Commit and read to sync results accross clients
				NetPack();
				NetRead();
			}
		}
		else
		{
			cue_vdir = new Vector2( cuetip.transform.forward.z, -cuetip.transform.forward.x ).normalized;

			// Get where the cue will strike the ball
			if( RayCircle( lpos2, cue_vdir, ball_positions[0] ))
			{
				guideline.SetActive( true );
				devhit.SetActive( true );
				devhit.transform.position = new Vector3( RayCircle_output.x, 0.0f, RayCircle_output.y );

				Vector2 scuffdir = ( ball_positions[0] - RayCircle_output ).normalized * 0.5f;

				cue_vdir += scuffdir;
				cue_vdir = cue_vdir.normalized;

				// TODO: add scuff offset to vdir
				cue_fdir = Mathf.Atan2( cue_vdir.y, cue_vdir.x );

				// Update the prediction line direction
				guideline.transform.eulerAngles = new Vector3( 0.0f, -cue_fdir * Mathf.Rad2Deg, 0.0f );
			}
			else
			{
				devhit.SetActive( false );
				guideline.SetActive( false );
			}
		}

		cue_llpos = lpos2;
	}

	private void Start()
	{
		// randomize positions and velocities
		for( int i = 0; i < 16; i ++ ) 
		{
			ball_originals[i].x = balls_render[i].transform.position.x;
			ball_originals[i].y = balls_render[i].transform.position.z;
		}

		SendDebugImpulse();
	}

	public void SetupBreak()
	{
		sn_pocketed = 0x00;

		for( int i = 0; i < 16; i ++ )
		{
			ball_positions[ i ] = ball_originals[ i ];
			ball_velocities[ i ] = Vector2.zero;
			balls_render[ i ].SetActive( true );
		}
	}

	public void SendDebugImpulse()
	{
		SetupBreak();

		//ball_positions[0].x = -0.758f;
		//ball_positions[0].y = 0.0f;

		//ball_velocities[0].x = UnityEngine.Random.Range( 0.5f, 8.0f );
		//ball_velocities[0].y = UnityEngine.Random.Range( -0.06f, 0.06f );

		// Rencode positions to ensure truncation is the same on all clients
		NetPack();
		NetRead();
	}

	// REGION NETWORKING
	// =========================================================================================================================

	const float I16_MAXf = 32767.0f;

	// 2 char string from unsigned short
	string EncodeUint16( ushort sh )
	{
		string enc = "";
		enc += (char)(((uint)sh) & 0xFF);
		enc += (char)(((uint)sh >> 8) & 0xFF);
		return enc;
	}

	// 4 char string from Vector2. Encodes floats in: [ -range, range ] to 0-65535
	string Encodev2( Vector2 vec, float range )
	{
		ushort x = (ushort)((vec.x / range) * I16_MAXf + I16_MAXf );
		ushort y = (ushort)((vec.y / range) * I16_MAXf + I16_MAXf );

		return EncodeUint16(x) + EncodeUint16(y);
	}

	// 2 chars at index to ushort
	ushort DecodeUint16( char[] arr, int start )
	{
		ushort dec = 0x00;
		dec |= (ushort)((arr[start + 0]) & 0x00FF);
		dec |= (ushort)(((uint)(arr[start + 1]) << 8) & 0xFF00);
		return dec; 
	}

	// Decode 4 chars at index to Vector2. Decodes from 0-65535 to [ -range, range ]
	Vector2 Decodev2( char[] arr, int start, float range )
	{
		float x = (((float)DecodeUint16(arr, start) - I16_MAXf) / I16_MAXf) * range;
		float y = (((float)DecodeUint16(arr, start + 2) - I16_MAXf) / I16_MAXf) * range;
		return new Vector2(x,y);
	} 
	 
	// Encode all data of game state into netstr
	public void NetPack()
	{
		string enc = "";

		// positions
		for ( int i = 0; i < 16; i ++ )
		{
			string coded = Encodev2(ball_positions[i], 2.5f);
			enc += coded;

			Vector2 test = Decodev2(coded.ToCharArray(), 0, 2.5f);
		}

		// Cue ball velocity last
		enc += Encodev2( ball_velocities[0], 50.0f );

		netstr = enc;

		Debug.Log( "[ht8b] NETPACK: " + netstr );
	}

	// Decode networking string
	public void NetRead()
	{
		Debug.Log("[ht8b] NETREAD: " + netstr);

		if (netstr.Length < 17 * 4)
		{
			Debug.Log("Sync string too short for decode, skipping\n");
			return;
		}

		char[] arr = netstr.ToCharArray();

		for (int i = 0; i < 16; i++)
		{
			ball_velocities[i] = Vector2.zero;
			ball_positions[i] = Decodev2(arr, i * 4, 2.5f);
		}

		ball_velocities[0] = Decodev2(arr, 16 * 4, 50.0f);

		ltext.text = ( Networking.IsOwner(Networking.LocalPlayer, this.gameObject) ? "[OWNER] @" : "[RECVR] @" ) + Time.time.ToString() + ": " + netstr_hex();
	}

	string netstr_hex()
	{
		char[] arr = netstr.ToCharArray();
		string str = "";

		for( int i = 0; i < netstr.Length / 2; i ++ )
		{
			ushort v = DecodeUint16( arr, i * 2 );
			str += v.ToString("X4");
		}

		return str;
	}

	// Wait for updates to the synced netstr
	public override void OnDeserialization()
	{
		if( !string.Equals( netstr, netstr_prv ) )
		{
			Debug.Log( "netstr update" );
			netstr_prv = netstr;
			NetRead();
		}
	}
}
