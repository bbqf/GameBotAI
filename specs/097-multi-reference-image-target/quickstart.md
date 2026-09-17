# Quickstart: Alternate Reference Images (097)

1. Upload the night crops as ordinary images:
   `POST /api/images` `{ "id": "pns-resource-cluster-anchor-night1", "data": "<base64 png>" }` (repeat).
2. Attach them to the daylight anchor:
   `PUT /api/images/pns-resource-cluster-anchor/alternates`
   `{ "alternates": ["pns-resource-cluster-anchor-night1", "pns-resource-cluster-anchor-night2", "pns-resource-cluster-anchor-night3"] }`
3. Verify on a night capture:
   `POST /api/images/detect` `{ "referenceImageId": "pns-resource-cluster-anchor", "captureId": "<id>", "threshold": 0.85 }`
   → a match whose `matchedReferenceId` is one of the night crops.
4. Every sequence condition, wait-for-image step, image-anchored tap and trigger naming
   `pns-resource-cluster-anchor` now covers the night crops; the three extra OR'd `Break` steps can be
   removed from the sequence (in the automation repo, not by this feature).
5. Crop alternates centred on the same art as the primary: tap offsets are applied to the centre of
   whichever reference matched.
