# PackedColumn

## Description (WIP)
CAPE-OPEN Unit Operation for building Digital Twins of Packed Columns using Neural Nets and manufacturer data.

## Requirements, Build & Installation

### 1. Requirements

The following software and components are required to build and use the Packed Column Twin CAPE-OPEN Unit Operation.

#### Development requirements

- **Microsoft Visual Studio** – with the .NET desktop development workload installed.
- **.NET Framework 4.x** – compatible with the target framework configured in the Visual Studio project.
- **MOSE CAPE-OPEN .NET Toolkit** – required for implementing the CAPE-OPEN interfaces and unit operation functionality.
- **Windows** – required for COM registration and execution within a compatible Process Modelling Environment (PME).

#### Runtime requirements

- A CAPE-OPEN-compliant Process Modelling Environment (PME).
- The appropriate .NET Framework runtime.
- The MOSE Toolkit dependencies required by the unit operation.
- Matching process architecture (x86 or x64) between the unit operation and the PME.

### 2. Build Instructions

1. Clone or download this repository.
2. Open the solution (`.sln`) in Microsoft Visual Studio.
3. Ensure that all required MOSE Toolkit references and dependencies are available.
4. Select the appropriate build configuration:
   - `Release` for deployment.
   - `Debug` for development and debugging.
5. Select the target platform (`x86` or `x64`) to match the intended PME.
6. Build the solution using **Build → Build Solution**.

The compiled unit operation DLL will be generated in the project's build output directory, typically:

```text
bin/Release/
```

#### Automatic COM Registration

The project can be configured to register the compiled assembly automatically after a successful build using a Visual Studio post-build event.

For example, for a 64-bit .NET Framework installation:

```cmd
"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe" "$(TargetPath)" /codebase /tlb
```

For a 32-bit registration:

```cmd
"C:\Windows\Microsoft.NET\Framework\v4.0.30319\RegAsm.exe" "$(TargetPath)" /codebase /tlb
```

Visual Studio should be run with administrator privileges if registry permissions require elevation.

The assembly implements COM registration callbacks to register the CAPE-OPEN component categories and associated component metadata.

### 3. Installation

The unit operation must be registered as a COM component before it can be discovered and used by a CAPE-OPEN-compliant PME.

#### 3.1 Manual COM Registration

Open **Command Prompt as Administrator** and execute the appropriate command.

**64-bit registration:**

```cmd
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe "C:\Path\To\PackedColumnTwin.dll" /codebase /tlb
```

**32-bit registration:**

```cmd
C:\Windows\Microsoft.NET\Framework\v4.0.30319\RegAsm.exe "C:\Path\To\PackedColumnTwin.dll" /codebase /tlb
```

Replace the DLL path with the actual location of the compiled assembly.

The `/codebase` option registers the assembly location in the Windows registry, while `/tlb` generates and registers the COM type library.

#### 3.2 CAPE-OPEN Metadata Registration

During COM registration, the unit operation's registration callback reads the assembly-level `CapeOpenUnitInfoAttribute` attributes and registers the component metadata.

For example:

```csharp
[assembly: CapeOpenUnitInfoAttribute(
    CapeOpenUnitInfoEnum.Name,
    "Packed Column Twin")]

[assembly: CapeOpenUnitInfoAttribute(
    CapeOpenUnitInfoEnum.Description,
    "Digital twin for packed column simulation")]
```

This information is registered under the component's `CapeDescription` registry key.

The registration callback also registers the CAPE-OPEN component categories required for PME discovery.

#### 3.3 Verify Installation

After registration:

1. Open the target Process Modelling Environment.
2. Locate the available CAPE-OPEN Unit Operations.
3. Find **Packed Column Twin** in the component list.
4. Add the unit operation to a flowsheet.
5. Verify that its configuration and parameter interfaces are accessible.

If the unit operation does not appear, verify the COM registration, CAPE-OPEN component categories, architecture compatibility and assembly dependencies.

### 4. Uninstallation / COM Unregistration

To remove the unit operation from the system, close any running PME instances and execute the following command from an elevated Command Prompt.

**64-bit unregistration:**

```cmd
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe "C:\Path\To\PackedColumnTwin.dll" /unregister
```

**32-bit unregistration:**

```cmd
C:\Windows\Microsoft.NET\Framework\v4.0.30319\RegAsm.exe "C:\Path\To\PackedColumnTwin.dll" /unregister
```

The assembly's `ComUnregisterFunction` callback removes the custom CAPE-OPEN registry entries, including:

- CAPE-OPEN Unit Operation component category.
- CAPE-OPEN Object component category.
- CAPE-OPEN component description metadata (`CapeDescription`).

RegAsm removes the standard COM registration entries that it manages.

After successful unregistration, the unit operation should no longer be discoverable by the PME.

**Important:** Unregister the assembly before deleting or moving the DLL, since RegAsm needs to load the assembly to execute its unregistration callback.

### 5. Deployment Notes

- Ensure that the registered DLL remains at the location specified during COM registration.
- Use the same architecture as the target PME (x86 or x64).
- Register the assembly again whenever its installation location changes.
- Avoid registering multiple copies of the same CLSID from different locations.
- Close the PME before replacing or unregistering the DLL.
- Ensure that all MOSE Toolkit and other required dependencies are available at runtime.