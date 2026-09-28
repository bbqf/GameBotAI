# Quickstart: Press and hold at a detected point

1. Make a command with an anchored press and hold:

   ```http
   POST /api/commands
   {
     "name": "Take free claims",
     "steps": [
       {
         "type": "PrimitiveTap",
         "order": 0,
         "primitiveTap": {
           "detectionTarget": { "referenceImageId": "claim-button", "confidence": 0.9 },
           "holdMs": 700
         }
       }
     ]
   }
   ```

2. Read the command back with `GET /api/commands/{id}`. The step has `"holdMs": 700`.

3. Run the command with `POST /api/commands/{id}/force-execute`. The step outcome has `"holdMs": 700` and the point.

4. Open the execution log entry of the run. The `tap` detail tells "Press and hold at (x,y) for 700 ms." and has the attribute `holdMs`.

5. Send `"holdMs": 5001`. The service returns 400 with "primitiveTap.holdMs must be between 0 and 5000".

6. In the web UI, open the command, edit the tap step, and set "Hold duration (ms)". Save. The step list shows "hold 700 ms".
