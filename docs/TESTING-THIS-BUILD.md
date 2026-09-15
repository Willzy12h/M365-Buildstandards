# Testing this build

Everything here can be done on a Windows machine with no tenant at all, up to the point where you choose to connect one. Read the first two sections before you connect anything.

## 1. Get the build

Every green run of **Build and test** publishes a ready-to-run package. You do not need Visual Studio or the .NET SDK.

1. Open the repository's **Actions** tab and select the most recent successful run on `integration`.
2. Download the **portable-windows-review** artifact. It contains the application ZIP and its `.sha256` checksum.
3. Unzip it, then **unblock** it: right-click the ZIP before extracting, tick *Unblock*, and extract. Windows marks downloaded files, and an unblocked folder saves a warning on every launch.
4. Run `M365-BuildStandard-Tool.exe`.

To check the download, compare the `.sha256` file with:

```powershell
Get-FileHash .\M365-BuildStandard-Tool-*.zip -Algorithm SHA256
```

The same run also publishes **synthetic-ui-review**, which is a set of screenshots of every main page rendered offline at two window sizes, plus `binding-errors.txt`. If you only want to review the interface, those images are enough and you do not have to run anything.

## 2. What it will and will not do

- Nothing is enabled or assigned automatically. Conditional Access policies are created **disabled**, device policies are created **unassigned**, groups are created **empty**, and the office named location is created **untrusted**.
- Assessment mode is read-only and holds no write permission at the token level. Deployment is a separate application with separate consent.
- Use a test or disposable tenant for anything beyond read-only. This is a preview: no payload in it has yet been accepted by Microsoft Graph.

## 3. Review the interface without a tenant

Launch the application and go straight to these two pages.

**Build Standard.** Choose a release, browse the controls, and read the detail panel. Then type a client name into *Prepared for* and select **Export document (HTML)**. That writes the client-facing build standard to your reports folder. Open it in a browser; it prints to PDF cleanly.

**Policy automation → Policy inputs and imports.** This is the generated input form. Each field shows what it accepts and its current status: supplied, needed, or which default applies. Type a deliberately wrong value, for example `abc` in a field asking for object identifiers, and confirm the problem appears beside that field rather than as a dialogue.

## 4. Connect a tenant read-only

1. On **Connect**, enter a client label and the tenant ID, then choose **Set up or validate applications**. Follow [application setup](APPLICATION-SETUP.md); it creates two dedicated registrations and walks you through consent.
2. Choose **Continue read-only**.
3. **Overview and licences** loads subscription counts. **Capture** reads the tenant configuration.
4. **Assessment** compares the capture with the standard. Export the engineer report in any format.

Nothing has been written to the tenant at this point.

## 5. Try a deployment in a disposable tenant

Deployment mode now also requests permission to create groups, so **both registrations need administrator consent again** before this works.

Do it in this order, because everything else depends on the first step:

1. **Prerequisites first.** Plan and create the exclusion groups and the office named location. The named location needs the office public IP ranges as a client input.
2. Record the created group and location identifiers in the client profile inputs.
3. Plan the controls you want. Read each plan row: it states the exact object to be created, the safe state it will be created in, and any warning, including where a shipped default was used instead of a value you supplied.
4. Execute. Each write is recorded with before and after evidence.
5. Activation and assignment are separate reviewed actions on **Policy automation → Tenant changes and targeting**. Nothing you created in step 4 is doing anything until you take that step deliberately.
6. **Undo and recovery** can delete or restore what the toolkit created, from its own change register.

## 6. What is worth your feedback

- Does the input form ask for things in language that makes sense to an engineer who has not read the code?
- Are the plan row warnings clear enough to act on, particularly the one about a shipped default?
- Does the client document read the way you would want to send it, especially the "Without it" wording?
- Anything that needs an identifier pasted in where it could have offered you a list instead.

## 7. Known limits in this preview

- No payload has been accepted by a live tenant yet. The first real capture may show that a Graph shape differs from what the standard assumes; the fix belongs in the standard file, not the code.
- OneDrive and Edge configuration, the four Microsoft Store applications, and the two agent packages cannot be created until you supply their inputs.
- Windows Autopatch can only be read in deployment mode, because Microsoft exposes no read-only permission for it.
- Group membership is never written. The toolkit creates groups empty and reports who is in them.
