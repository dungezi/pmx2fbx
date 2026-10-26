# Third-party dependencies and research references

The PMX and XPS readers, converter, UI and shaders in this package are independently
implemented. No saba, Unity MMD Tools or XNALaraMesh implementation files are vendored.
Their repositories were consulted as references to PMX binary layout,
coordinate conventions and Unity humanoid APIs; see README for links.
XNALaraMesh (https://github.com/johnzero7/XNALaraMesh) was consulted for the
XPS binary/ASCII layout and render-group alpha flags; its Blender scripts
are not redistributed.

The independent VRM/GLB reader and JSON parser were written against the VRM
Consortium specifications (https://github.com/vrm-c/vrm-specification) and
Khronos glTF 2.0 specification (https://registry.khronos.org/glTF/specs/2.0/glTF-2.0.html).
No UniVRM or third-party JSON implementation is bundled.

Unity FBX Exporter (`com.unity.formats.fbx`, 4.2.1) is resolved separately by
Unity Package Manager. It is distributed under the Unity Companion License.
Its Autodesk FBX SDK dependency (`com.autodesk.fbx`) has its own license and
third-party notices. Consult the LICENSE and Third Party Notices files in
those installed packages. They are not covered by this repository's MIT
license and are not bundled in the plugin release archive.

VRChat Avatars SDK is optional, installed by the user through Creator
Companion. No SDK binaries or code are bundled in the plugin release.
