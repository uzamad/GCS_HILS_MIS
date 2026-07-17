// ================================================================
//  CommandState.cs  –  GCS_240626
//
//  Holds the GCS → FCC command link state.  One instance lives in
//  CommandLinkSender and is encoded into 36-byte packets by
//  CommandPacketEncoder every 100 ms.
//
//  All bool fields map 1:1 to bits in apio_decode.c
//  Command_MC_Buffer[] as documented below.
// ================================================================

namespace GCS_240626
{
    public class CommandState
    {
        // ── Byte 5  AP Byte 1 ────────────────────────────────────────
        public bool IncParam;               // bit 0
        public bool DecParam;               // bit 1
        public bool SetDefaultLonGains;     // bit 2
        public bool SetDefaultLatGains;     // bit 3
        public bool HeightControlScheme;    // bit 4  (0=normal, 1=ROC)
        public bool UseAduSpeed;            // bit 5

        // ── Byte 6  AP Byte 2 ────────────────────────────────────────
        public bool EnableGainTuning;               // bit 4
        public bool EnableStabilityAugmentation;    // bit 5
        public bool UseNonLinearRollControl;        // bit 6
        public bool UseFlapsAsAilerons;             // bit 7

        // ── Byte 7  Landing Byte ─────────────────────────────────────
        public bool TODirection;            // bit 0
        public bool Landing;                // bit 1
        public bool LandingDirection;       // bit 2
        public bool DummyLanding;           // bit 3
        public bool AbortLanding;           // bit 4
        public bool DenyHitCriteria;        // bit 5
        public bool EmergencyLanding;       // bit 6
        public bool NLRetractionCmd;        // bit 7

        // ── Byte 8  Navigation Byte ──────────────────────────────────
        public bool GoToNextWP;             // bit 0
        public bool GoToPrevWP;             // bit 1
        public bool LeftXTrackCorrection;   // bit 2
        public bool RightXTrackCorrection;  // bit 3
        public bool UseCurveGuidance;       // bit 4
        public int  LateralGuidanceScheme;  // bits 5-6  (0-3)
        public bool Loiter;                 // bit 7

        // ── Byte 9  Vehicle Control Byte ─────────────────────────────
        public bool UseGpsSpeed;            // bit 0
        public bool GcsAscend;              // bit 1
        public bool GcsDescend;             // bit 2
        public bool GcsIncrementSpeed;      // bit 3
        public bool GcsDecrementSpeed;      // bit 4
        public bool UseGpsAlt;              // bit 5
        public bool UsePresAlt;             // bit 6
        public bool UseRadarAlt;            // bit 7

        // ── Byte 10  Mixed Data Byte ─────────────────────────────────
        public bool GcsR2Base;              // bit 1
        public bool Search;                 // bit 2  — initiate search pattern
        public bool FullThrottle;           // bit 3
        public bool PilotAugmentationOff;   // bit 4  (1 = pilot augmentation disabled)
        public bool Eco;                    // bit 6
        public bool EngineKill;             // bit 7

        // ── Byte 11  Auxiliary Byte ──────────────────────────────────
        public bool BrakesOn;              // bit TBD
        public bool Switch2BupLink;         // bit 0
        public bool RetractNoseLandingGear; // bit 1
        public bool GcsRunupBypass;         // bit 2
        public bool EnableFccOverride;      // bit 3
        public bool UseFcc;                 // bit 6
        public bool ElevFilterEnable;       // bit 7

        // ── Byte 12  Taxi Control ────────────────────────────────────
        public byte TaxiControlCmd;

        // ── Byte 13  Inc/Dec param select ───────────────────────────
        public byte SelectIncDecParam;

        // ── Byte 14  Vehicle Mode ────────────────────────────────────
        public byte VehicleMode;

        // ── Byte 15  AP Byte 3 ───────────────────────────────────────
        public bool FlapsDown;              // bit 0
        public bool AltitudeHold;           // bit 2
        public bool SpeedHold;              // bit 3
        public bool EnableLogging;          // bit 4
        public bool AirModesEnabledSwt;     // bit 5  — AIR_MODES_ACTIVE
        public bool GndCrewClearanceSwt;    // bit 6  — takeoff clearance

        // ── Bytes 16-18  NYI (SLT / SysID) ─────────────────────────
        // left as zero

        // ── Byte 19  Mixed Byte 2 ────────────────────────────────────
        public bool DenyAbortTaxi;          // bit 0  — bypass GPS abort check
        public bool DgpsCorrectionsEnabled; // bit 1
        public bool EnableTurnCompensation; // bit 2
        public bool EnableTelemetryLink;    // bit 3  — gate for TX telemetry
        public bool EnablePfEstimation;     // bit 4
        public bool SetDefaultGndGains;     // bit 5
        public bool EnableFixedMission;     // bit 6
        public bool ReleaseBrakes;          // bit 7

        // ── Byte 20  Telemetry MPC ───────────────────────────────────
        public byte TelemetryMpc;

        // ── Byte 21  Vehicle ID ──────────────────────────────────────
        public byte VehicleId = 2;          // must match VEHICLE_ID in apconst.h

        // ── Bytes 22-24  IPSU Relay Control ─────────────────────────
        public byte RelayControlByte1;
        public byte RelayControlByte2;
        public byte RelayControlByte3;

        // ── Byte 25  Brake command ───────────────────────────────────
        public byte BrakeCmd;

        // ── Bytes 26-27  Spare ───────────────────────────────────────
        // always zero

        // ── HILS-safe defaults ───────────────────────────────────────
        /// <summary>
        /// Apply defaults that make the sim safe for HILS testing:
        ///   - Telemetry enabled so the GCS receives data immediately
        ///   - Deny abort-taxi so the missing GPS / DGPS doesn't abort ground run
        ///   - Vehicle ID = 2 (VEHICLE_ID in apconst.h)
        /// All other bits default to false / 0.
        /// </summary>
        public void ApplyHilsDefaults()
        {
            EnableTelemetryLink = true;
            DenyAbortTaxi       = true;
            VehicleId           = 2;
            VehicleMode         = 3;      // STANDBY — safe ground default
            UseGpsAlt           = true;   // GPS is default altitude sensor
            UsePresAlt          = false;
            UseGpsSpeed         = true;   // GPS is default airspeed source
            UseAduSpeed         = false;
        }
    }
}
