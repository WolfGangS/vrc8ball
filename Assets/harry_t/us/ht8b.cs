//#define USE_FIXED_POINT

using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.Udon;
using System;

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
	public bool bArmed = false;

	[SerializeField]
	Vector2 extraGravy;

	[SerializeField]
	public GameObject[] playerTotems;

	// REGION GAME STATE
	// =========================================================================================================================
	public bool	sn_simulating = false;	// True whilst balls are rolling
	public uint	sn_pocketed = 0x00;		// Each bit represents each ball, if it has been pocketed or not

	public bool	sn_updatelock = false;	// We are waiting for our local simulation to finish, before we unpack data
	public uint	sn_turnid = 0x00U;		// Whos turn is it, 0 or 1
	public bool	sn_permit = false;		// Permission for local player to play
	public int	sn_firsthit = 0;        // The first ball to be hit by cue ball
	public bool sn_foul = false;			// End-of-turn foul marker
	
	// REGION PHYSICS ENGINE
	// =========================================================================================================================

	bool ballsMoving = false;

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
	const float BALL_1OR = 16.66666666666666f;
	const float BALL_RSQR = 0.0009f;
	const float POCKET_RADIUS = 0.09f;
	const float K_1OR2 = 0.70710678118f;   // 1 over root 2
	const float K_1OR5 = 0.4472135955f;    // 1 over root 5
	const float POCKET_DEPTH = 0.04f;
	const float MIN_VELOCITY = 0.00005625f;	// ( SQUARED )

	const float FRICTION_EFF = 0.99f;

	public Vector2[]	ball_positions = new Vector2[16];
	Vector2[]			ball_originals = new Vector2[16];
	public Vector2[] ball_velocities = new Vector2[16];

	// Components
	AudioSource aud_click;

	const string FRP_LOW =	"<color=\"#ADADAD\">";
	const string FRP_ERR =	"<color=\"#B84139\">";
	const string FRP_WARN = "<color=\"#DEC521\">";
	const string FRP_YES =	"<color=\"#69D128\">";
	const string FRP_END =	"</color>";

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

				ball_positions[ id ] = new Vector2( i, j );

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

		Vector2 mov_delta = ball_velocities[id] * FIXED_TIME_STEP;
		float mov_mag = mov_delta.magnitude;

		// Apply movement
		ball_positions[ id ] += mov_delta;

		// Rotate visual object by pure rolling
		balls_render[ id ].transform.Rotate( new Vector3( mov_delta.y, 0.0f, -mov_delta.x ) / mov_mag, mov_mag * BALL_1OR * Mathf.Rad2Deg, Space.World );

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

					//aud_click.volume = Mathf.Clamp( ball_velocities[id].sqrMagnitude * 0.2f, 0.0f, 1.0f ); 
					aud_click.Play();

					// First hit detected
					if( id == 0 && sn_firsthit == 0 )
					{
						sn_firsthit = i;
					}
				}
			}
		}

		// ball still moving about
		if( ball_velocities[ id ].sqrMagnitude > MIN_VELOCITY )
		{
			ballsMoving = true;
		}
		else
		{
			// Put velocity to 0
			ball_velocities[ id ] = Vector2.zero;
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

	void NewTurn()
	{
		FRP( FRP_YES + "NewTurn()" + FRP_END );

		// Fixup game state
		if( sn_foul )
		{
			FRP( FRP_LOW + "Game state fixup" + FRP_END );

			if( (sn_pocketed & 0x1U) == 0x1U )
			{
				ball_positions[0] = ball_originals[0];
				ball_velocities[0] = Vector2.zero;

				// https://vrchat.canny.io/vrchat-udon-closed-alpha-feedback/p/bitwisenot-for-integer-built-in-types
				// sn_pocketed &= ~0x1U;

				sn_pocketed &= 0xFFFFFFFEU;
			}
		}

		sn_permit = true;
		sn_foul = false;
		sn_firsthit = 0;
	}

	void SimEnd()
	{
		sn_simulating = false;

		FRP( FRP_LOW + "(local) SimEnd()" + FRP_END );

		if( Networking.GetOwner( this.gameObject ) == Networking.LocalPlayer )
		{
			// Owner state checks
			FRP( FRP_LOW + "Post-move state checking" + FRP_END );

			// Check for fouls
			if( (sn_pocketed & 0x1U) == 0x1U )
			{
				FRP( FRP_ERR + "FOUL: scratched" + FRP_END );
				sn_foul = true;
			}
			else
			{
				// Check first hit rules
				// No hit
				if ( sn_firsthit == 0 )
				{
					FRP( FRP_ERR + "FOUL: cue diddn't hit anything" + FRP_END );
					sn_foul = true;
				}
				else
				{
					// Currently no check for spot/stripes, but is for 8 ball ALWAYS
					// todo: allow this when player is on final
					if ( sn_firsthit == 1 )
					{
						FRP( FRP_ERR + "FOUL: cue hit 8 first" + FRP_END );
						sn_foul = true;
					}
				}
			}

			if( sn_foul )
			{
				// Flip player bit and commit, reciever will take ownership once update propogates
				FRP( FRP_LOW + "Transferring ownership" + FRP_END );

				NetPack( sn_turnid ^ 0x1U );
				NetRead();
			}
			else
			{
				FRP( FRP_YES + "Legal move confirmed" + FRP_END );

				// Everything was fine, player can go againf
				NewTurn();
			}
		}
		else
		{
			// Check if there was a network update on hold
			if( sn_updatelock )
			{
				FRP( FRP_LOW + "Update was waiting, executing now" + FRP_END );
				sn_updatelock = false;

				NetRead();
			}
		}
	}

	void PhysicsUpdate()
	{
		ballsMoving = false;

		// Run main simulation / inter-ball collision
		for( int i = 0; i < 16; i ++ )
		{
			BallSimulate( i );
		}

		// Check if simulation has settled
		if( !ballsMoving )
		{
			if( sn_simulating )
			{
				SimEnd();
			}

			return;
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
		
		// Run sim only if things are moving
		if( sn_simulating )
		{
			accum += timeDelta;

			while ( accum >= FIXED_TIME_STEP )
			{
				PhysicsUpdate();
				accum -= FIXED_TIME_STEP;
			}
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
		
		// Check if we are allowed to play
		if( sn_permit )
		{
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

					// Remove locks
					bArmed = false;
					sn_permit = false;

					FRP( FRP_LOW + "Commiting changes" + FRP_END );

					// Commit changes
					sn_simulating = true;
					NetPack( sn_turnid );
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
		}

		cue_llpos = lpos2;
	}

	private void Start()
	{
		FRP( FRP_LOW + "Starting" + FRP_END );

		aud_click = this.GetComponent<AudioSource>();

		// randomize positions and velocities
		for( int i = 0; i < 16; i ++ ) 
		{
			ball_originals[i].x = balls_render[i].transform.position.x;
			ball_originals[i].y = balls_render[i].transform.position.z;
		}

		SetupBreak();

		NetPack( 0 );
		NetRead();
	}

	// Resets local game state to defined state
	public void SetupBreak()
	{
		FRP( FRP_LOW + "SetupBreak()" + FRP_END );

		sn_pocketed = 0x00;
		sn_simulating = false;

		for( int i = 0; i < 16; i ++ )
		{
			ball_positions[ i ] = ball_originals[ i ];
			ball_velocities[ i ] = Vector2.zero;
			balls_render[ i ].SetActive( true );
		}
	}

	public void SendDebugImpulse()
	{
		FRP( "Resetting" );

		SetupBreak();

		// Re-encode positions
		NetPack( 0 );
		NetRead();
	}

	public void NewGame()
	{
		FRP( FRP_LOW + "(local) NewGame()" + FRP_END );

		if( Networking.GetOwner( playerTotems[0] ) == Networking.LocalPlayer )
		{
			FRP( FRP_YES + "Starting new game" + FRP_END );
			
			Networking.SetOwner( Networking.LocalPlayer, this.gameObject );

			SetupBreak();
			NewTurn();

			// TODO: send which totem ID started the game instead
			NetPack( 0 );
			NetRead();
		}
		else
		{
			FRP( FRP_ERR + "Permission denied, you are not player 0" + FRP_END );
		}
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
	public void NetPack( uint _turnid )
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

		// Encode pocketed imformation
		enc += EncodeUint16( (ushort)(sn_pocketed & 0x0000FFFFU) );

		// Game state
		uint flags = 0x0U;
		if( sn_simulating ) flags |= 0x1U;
		flags |= _turnid << 1;
		if( sn_foul ) flags |= 0x4U;

		enc += EncodeUint16( (ushort)flags );

		netstr = enc;

		FRP( FRP_LOW + "NetPack()" + FRP_END );
	}

	// Decode networking string
	public void NetRead()
	{
		FRP( FRP_LOW + netstr_hex() + FRP_END );

		if( netstr.Length < 18 * 4 )
		{
			FRP( FRP_WARN + "Sync string too short for decode, skipping\n" + FRP_END );
			return;
		}

		char[] arr = netstr.ToCharArray();

		for( int i = 0; i < 16; i ++ )
		{
			ball_velocities[i] = Vector2.zero;
			ball_positions[i] = Decodev2( arr, i * 4, 2.5f );
		}

		ball_velocities[0] = Decodev2( arr, 16 * 4, 50.0f );

		// Pocketed information
		sn_pocketed = DecodeUint16( arr, 17 * 4 );

		// Game state
		uint gamestate = DecodeUint16( arr, 17 * 4 + 2 );
		sn_simulating = (gamestate & 0x1U) == 0x1U;
		sn_foul = (gamestate & 0x4U) == 0x4U;

		uint newturn = (gamestate & 0x2U) >> 1;
		if( sn_turnid != newturn )
		{
			FRP( FRP_LOW + "Ownership changed" + FRP_END );

			sn_turnid = newturn;

			// Fullfil ownership transfer
			if( Networking.GetOwner( playerTotems[ sn_turnid ] ) == Networking.LocalPlayer )
			{
				FRP( FRP_YES + "Transfered to local" + FRP_END );

				if( sn_simulating )
				{
					// In THEORY this should never ever be hit, but there might be an edge case
					FRP( FRP_ERR + "Remote still simulating when ownership transfer attempt was made... script is deadlocked! contact harry!" + FRP_END );
				}
				else
				{
					// Give our local player permission to play his turn
					Networking.SetOwner(Networking.LocalPlayer, this.gameObject);
					
					// Sort out gamestate
					NewTurn();
					
					// Not sure why these were called ?
					// NetPack( sn_turnid );
					// NetRead();
				}
			}
			else
			{
				FRP( FRP_LOW + "Transfered to remote" + FRP_END );
			}
		}
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
			FRP( FRP_LOW + "OnDeserialization() :: netstr update" + FRP_END );

			netstr_prv = netstr;

			// Check if local simulation is in progress, the event will fire off later when physics
			// are settled by the client
			if( sn_simulating )
			{
				FRP( FRP_WARN + "local simulation is still running, the network update will occur after completion" + FRP_END );
				sn_updatelock = true;
			}
			else
			{
				// We are free to read this update
				NetRead();
			}
		}
	}

	const int FRP_MAX = 32;
	int FRP_LEN = 0;
	int FRP_PTR = 0;
	string[] FRP_LINES = new string[32];

	// Print a line to the debugger
	void FRP( string ln )
	{
		Debug.Log( "[<color=\"#B5438F\">ht8b</color>] " + ln );

		FRP_LINES[ FRP_PTR ++ ] = "[<color=\"#B5438F\">ht8b</color>] " + ln + "\n";
		FRP_LEN ++ ;

		if( FRP_PTR >= FRP_MAX )
		{
			FRP_PTR = 0;
		}

		if( FRP_LEN > FRP_MAX )
		{
			FRP_LEN = FRP_MAX;
		}

		string output = "";
		
		// Add information about game state:
		output += Networking.IsOwner(Networking.LocalPlayer, this.gameObject) ? 
			"<color=\"#95a2b8\">net(</color> <color=\"#4287F5\">OWNER</color> <color=\"#95a2b8\">)</color> ":
			"<color=\"#95a2b8\">net(</color> <color=\"#678AC2\">RECVR</color> <color=\"#95a2b8\">)</color> ";

		output += sn_simulating ?
			"<color=\"#95a2b8\">sim(</color> <color=\"#4287F5\">ACTIVE</color> <color=\"#95a2b8\">)</color> ":
			"<color=\"#95a2b8\">sim(</color> <color=\"#678AC2\">PAUSED</color> <color=\"#95a2b8\">)</color> ";

		output += "<color=\"#95a2b8\">player(</color> <color=\"#4287F5\">"+ Networking.GetOwner(playerTotems[sn_turnid]).displayName + ":" + sn_turnid + "</color> <color=\"#95a2b8\">)</color>";

		output += "\n---------------------------------------------------------------------------------------------------------------------------------------------------------\n";

		// Update display
		for( int i = 0; i < FRP_LEN ; i ++ )
		{
			output += FRP_LINES[ (FRP_MAX + FRP_PTR - FRP_LEN + i) % FRP_MAX ];
		}

		ltext.text = output;
	}
}
