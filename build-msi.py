"""Build the embedded Windows Installer database from the approved Iris ZIP inventory."""
import ctypes as ct
from ctypes import wintypes as wt
from pathlib import Path
import re
import subprocess
import uuid
import zipfile

ROOT = Path(__file__).resolve().parent
BUILD = ROOT / "build"
APP = ROOT / "app"
OUTPUT = BUILD / "Iris.msi"
UPGRADE = "{7E7B9118-284E-483D-AD6D-E0C8E06CB69C}"
MSI = ct.WinDLL("msi")


def function(name, *types):
    method = getattr(MSI, name)
    method.argtypes = types
    method.restype = wt.UINT
    return method


open_db = function("MsiOpenDatabaseW", wt.LPCWSTR, ct.c_void_p, ct.POINTER(wt.UINT))
import_table = function("MsiDatabaseImportW", wt.UINT, wt.LPCWSTR, wt.LPCWSTR)
open_view = function("MsiDatabaseOpenViewW", wt.UINT, wt.LPCWSTR, ct.POINTER(wt.UINT))
execute = function("MsiViewExecute", wt.UINT, wt.UINT)
set_string = function("MsiRecordSetStringW", wt.UINT, wt.UINT, wt.LPCWSTR)
set_stream = function("MsiRecordSetStreamW", wt.UINT, wt.UINT, wt.LPCWSTR)
get_summary = function("MsiGetSummaryInformationW", wt.UINT, wt.LPCWSTR, wt.UINT, ct.POINTER(wt.UINT))
set_summary = function("MsiSummaryInfoSetPropertyW", wt.UINT, wt.UINT, wt.UINT, wt.INT, ct.c_void_p, wt.LPCWSTR)
commit = function("MsiDatabaseCommit", wt.UINT)
persist_summary = function("MsiSummaryInfoPersist", wt.UINT)
close = function("MsiCloseHandle", wt.UINT)
create_record = function("MsiCreateRecord", wt.UINT)


def check(code, operation):
    if code:
        raise OSError(code, operation)


def table(db, name, columns, types, keys, rows):
    lines = [columns, types, (name, *keys), *rows]
    path = BUILD / (name + ".idt")
    path.write_bytes("".join("\t".join(map(str, line)) + "\r\n" for line in lines).encode("ascii"))
    check(import_table(db, str(BUILD), path.name), "Import " + name)


def stream(db, table_name, id_column, name, path):
    view = wt.UINT()
    sql = f"INSERT INTO `{table_name}` (`{id_column}`, `Data`) VALUES (?, ?)"
    check(open_view(db, sql, ct.byref(view)), "Open stream table")
    record = create_record(2)
    try:
        check(set_string(record, 1, name), "Set stream name")
        check(set_stream(record, 2, str(path)), "Read stream " + name)
        check(execute(view, record), "Insert stream " + name)
    finally:
        close(record)
        close(view)


def component_id(name):
    return "{" + str(uuid.uuid5(uuid.UUID(UPGRADE), name.lower())).upper() + "}"


def build():
    version_source = (APP / "source" / "ReleaseVersion.cs").read_text(encoding="utf-8")
    version = re.search(r'\bNumber = "([0-9]+\.[0-9]+\.[0-9]+)";', version_source).group(1)
    major, minor, patch = (int(part) for part in version.split("."))
    if not (0 < major <= 255 and minor <= 255 and patch <= 65535 and version == f"{major}.{minor}.{patch}"):
        raise ValueError("Version must be a canonical Windows Installer version")
    product = "{" + str(uuid.uuid5(uuid.UUID(UPGRADE), "product:" + version)).upper() + "}"
    BUILD.mkdir(exist_ok=True)
    (BUILD / "InstallerIdentity.g.cs").write_text(
        f'namespace IrisSetup {{ static class InstallerIdentity {{ internal const string ProductCode = "{product}"; }} }}\n',
        encoding="ascii",
    )
    if OUTPUT.exists():
        OUTPUT.unlink()
    files = (ROOT / "payload-files.txt").read_text(encoding="ascii").splitlines()
    manifest = "Iris.program-files.txt"
    files.append(manifest)
    (APP / manifest).write_text("\n".join(files) + "\n", encoding="ascii")

    directories = [
        ("TARGETDIR", "", "SourceDir"),
        ("ProgramFiles64Folder", "TARGETDIR", "."),
        ("INSTALLDIR", "ProgramFiles64Folder", "Iris"),
        ("ProgramMenuFolder", "TARGETDIR", "."),
    ]
    folder_ids = {(): "INSTALLDIR"}
    components, file_rows, feature_rows = [], [], []
    cabinet = BUILD / "Iris.cab"
    ddf = [
        ".OPTION EXPLICIT",
        ".Set Cabinet=on",
        ".Set Compress=on",
        ".Set CompressionType=MSZIP",
        ".Set MaxDiskSize=0",
        ".Set CabinetNameTemplate=Iris.cab",
        f'.Set DiskDirectoryTemplate="{BUILD}"',
        f'.Set InfFileName="{BUILD / "cabinet.inf"}"',
        f'.Set RptFileName="{BUILD / "cabinet.rpt"}"',
    ]
    exe_id = None
    for i, name in enumerate(files, 1):
        path = Path(name)
        if path.is_absolute() or any(part in {"..", "Data", "Data-qa"} for part in path.parts):
            raise ValueError("Unexpected payload path: " + name)
        directory = path.parent.parts
        for length in range(1, len(directory) + 1):
            key = directory[:length]
            if key not in folder_ids:
                identifier = "D%03d" % len(folder_ids)
                folder_ids[key] = identifier
                directories.append((identifier, folder_ids[key[:-1]], f"{identifier}|{key[-1]}"))
        file_id, component = f"F{i:04d}", f"C{i:04d}"
        if name == "Iris.exe":
            exe_id = file_id
        source = APP / path
        extension = path.suffix[:4]
        file_rows.append((file_id, component, f"{file_id}{extension}|{path.name}", source.stat().st_size, "", "", 16384, i))
        components.append((component, component_id(name), folder_ids[directory], 256, "", file_id))
        feature_rows.append(("Main", component))
        ddf.append(f'"{source}" {file_id}')
    if exe_id is None:
        raise ValueError("Iris.exe missing from the payload")
    components.append(("StartMenu", component_id("StartMenu"), "ProgramMenuFolder", 260, "", "StartMenuKey"))
    feature_rows.append(("Main", "StartMenu"))
    directives = BUILD / "Iris.ddf"
    directives.write_text("\n".join(ddf) + "\n", encoding="ascii")
    subprocess.run(["makecab.exe", "/F", str(directives)], cwd=BUILD, check=True, stdout=subprocess.DEVNULL)

    portable_output = BUILD / f"Iris-{major}.{minor}-Portable.zip"
    with zipfile.ZipFile(portable_output, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as portable:
        for name in files:
            portable.write(APP / name, name)

    db = wt.UINT()
    check(open_db(str(OUTPUT), ct.c_void_p(3), ct.byref(db)), "Create MSI")
    try:
        table(db, "Directory", ("Directory", "Directory_Parent", "DefaultDir"), ("s72", "S72", "l255"), ("Directory",), directories)
        table(db, "Component", ("Component", "ComponentId", "Directory_", "Attributes", "Condition", "KeyPath"), ("s72", "S38", "s72", "i2", "S255", "S72"), ("Component",), components)
        table(db, "File", ("File", "Component_", "FileName", "FileSize", "Version", "Language", "Attributes", "Sequence"), ("s72", "s72", "l255", "i4", "S72", "S20", "I2", "i2"), ("File",), file_rows)
        table(db, "Feature", ("Feature", "Feature_Parent", "Title", "Description", "Display", "Level", "Directory_", "Attributes"), ("s38", "S38", "L64", "L255", "I2", "i2", "S72", "i2"), ("Feature",), [("Main", "", "Iris", "Iris for Windows", 1, 1, "INSTALLDIR", 0)])
        table(db, "FeatureComponents", ("Feature_", "Component_"), ("s38", "s72"), ("Feature_", "Component_"), feature_rows)
        table(db, "Media", ("DiskId", "LastSequence", "DiskPrompt", "Cabinet", "VolumeLabel", "Source"), ("i2", "i4", "L64", "S255", "S32", "S72"), ("DiskId",), [(1, len(files), "", "#Iris.cab", "", "")])
        table(db, "Property", ("Property", "Value"), ("s72", "l0"), ("Property",), [
            ("ProductName", "Iris"), ("ProductVersion", version), ("ProductLanguage", "1033"), ("Manufacturer", "Iris"),
            ("ProductCode", product), ("UpgradeCode", UPGRADE), ("ALLUSERS", "1"), ("ARPNOMODIFY", "1"),
            ("ARPPRODUCTICON", "Iris.ico"), ("SecureCustomProperties", "INSTALLDIR;IRIS_OLDER;IRIS_NEWER"),
        ])
        table(db, "Icon", ("Name", "Data"), ("s72", "v0"), ("Name",), [])
        table(db, "Registry", ("Registry", "Root", "Key", "Name", "Value", "Component_"), ("s72", "i2", "l255", "L255", "L0", "s72"), ("Registry",),
              [("StartMenuKey", 2, r"Software\Iris\Installer", "Installed", "#1", "StartMenu"),
               ("InstallPath", 2, r"Software\Iris\Installer", "InstallLocation", "[INSTALLDIR]", "StartMenu")])
        table(db, "Shortcut", ("Shortcut", "Directory_", "Name", "Component_", "Target", "Arguments", "Description", "Hotkey", "Icon_", "IconIndex", "ShowCmd", "WkDir",
                               "DisplayResourceDLL", "DisplayResourceId", "DescriptionResourceDLL", "DescriptionResourceId"),
              ("s72", "s72", "l128", "s72", "s72", "S255", "L255", "I2", "S72", "I2", "I2", "S72", "S255", "I2", "S255", "I2"), ("Shortcut",),
              [("IrisShortcut", "ProgramMenuFolder", "Iris", "StartMenu", f"[#{exe_id}]", "", "Open Iris", "", "Iris.ico", 0, 1, "INSTALLDIR", "", "", "", "")])
        # Include the original, already-published 1.0 MSI in the first upgrade.
        # Maintenance of this MSI uses its own ProductCode and skips FindRelatedProducts.
        previous_attributes = 512 if version == "1.0.0" else 0
        table(db, "Upgrade", ("UpgradeCode", "VersionMin", "VersionMax", "Language", "Attributes", "Remove", "ActionProperty"),
              ("s38", "S20", "S20", "S255", "i4", "S255", "s72"), ("UpgradeCode", "VersionMin", "VersionMax", "Language", "Attributes"),
              [(UPGRADE, "", version, "", previous_attributes, "", "IRIS_OLDER"), (UPGRADE, version, "", "", 2, "", "IRIS_NEWER")])
        # The MSI service can report the Windows 8.1 version/build on Windows 10/11.
        # Setup.exe checks the OS version in its own manifested process.
        table(db, "LaunchCondition", ("Condition", "Description"), ("s255", "l255"), ("Condition",),
              [("Installed OR VersionNT64", "Iris requires 64-bit Windows."),
               ("Installed OR NOT IRIS_NEWER", "A newer version of Iris is already installed.")])
        table(db, "CustomAction", ("Action", "Type", "Source", "Target", "ExtendedType"), ("s72", "i2", "S72", "L0", "I4"), ("Action",),
              [("SetInstallLocation", 51, "ARPINSTALLLOCATION", "[INSTALLDIR]", "")])
        sequence_columns = ("Action", "Condition", "Sequence")
        sequence_types = ("s72", "S255", "I2")
        base_sequence = [("CostInitialize", "", 800), ("FileCost", "", 900), ("CostFinalize", "", 1000),
                         ("InstallValidate", "", 1400), ("InstallInitialize", "", 1500),
                         ("InstallFiles", "", 4000), ("InstallFinalize", "", 6600)]
        table(db, "InstallExecuteSequence", sequence_columns, sequence_types, ("Action",), base_sequence + [
            ("FindRelatedProducts", "", 25), ("LaunchConditions", "", 100), ("SetInstallLocation", "", 1100),
            ("RemoveExistingProducts", "", 1510), ("ProcessComponents", "", 1600), ("UnpublishFeatures", "", 1800),
            ("RemoveRegistryValues", "", 2600), ("RemoveShortcuts", "", 3200), ("RemoveFiles", "", 3500),
            ("RemoveFolders", "", 3600), ("CreateFolders", "", 3700), ("CreateShortcuts", "", 4500),
            ("WriteRegistryValues", "", 5000), ("RegisterUser", "", 6000), ("RegisterProduct", "", 6100),
            ("PublishFeatures", "", 6300), ("PublishProduct", "", 6400),
        ])
        table(db, "AdminExecuteSequence", sequence_columns, sequence_types, ("Action",), base_sequence + [("InstallAdminPackage", "", 3900)])
        stream(db, "_Streams", "Name", "Iris.cab", cabinet)
        stream(db, "Icon", "Name", "Iris.ico", APP / "Iris.ico")
        info = wt.UINT()
        check(get_summary(db, None, 12, ct.byref(info)), "Open MSI summary")
        try:
            for prop, value in {2: "Installation Database", 3: "Iris", 4: "Iris", 7: "x64;1033",
                                9: "{" + str(uuid.uuid4()).upper() + "}"}.items():
                check(set_summary(info, prop, 30, 0, None, value), "Set MSI string summary")
            for prop, value in {14: 500, 15: 2, 19: 2}.items():
                check(set_summary(info, prop, 3, value, None, None), "Set MSI numeric summary")
            check(persist_summary(info), "Save MSI summary")
        finally:
            close(info)
        check(commit(db), "Commit MSI")
    finally:
        close(db)
    print(f"{OUTPUT.name}: {len(files)} packaged files")


if __name__ == "__main__":
    build()
