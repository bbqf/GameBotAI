# Third-Party License Notices

This project includes third-party components. The following notices provide attribution and license context. Refer to the upstream repositories for full license texts.

## OpenCV (via OpenCvSharp4 runtime)
- Upstream Project: OpenCV
- License: BSD 3-Clause
- Source: https://github.com/opencv/opencv
- Summary: Permissive license allowing redistribution and use with minimal conditions (retain copyright notice, list of conditions, and disclaimer).

## OpenCvSharp4
- Repository: https://github.com/shimat/opencvsharp
- License: BSD 3-Clause (same terms as OpenCV)
- Purpose: .NET bindings and managed wrappers around OpenCV native libraries; simplifies template matching and image processing without external installation steps.

### Attribution
Redistributed binaries of OpenCV via the NuGet package `OpenCvSharp4.runtime.win` retain original licensing. No modifications have been made to these binaries.

### Usage Within GameBot
- Template matching (normalized cross-correlation) and preprocessing (grayscale conversion).
- Non-maximum suppression implemented in project code; not part of OpenCV APIs directly.

### Additional Notes
If you distribute GameBot, ensure this notice and the original BSD 3-Clause license texts remain accessible. For full license terms, consult the upstream repositories. No GPL/LGPL code from OpenCV optional modules is directly referenced.

---
If additional dependencies are added in the future, extend this file with their name, repository URL, license type, and usage purpose.
