// Builds the soak client from a git revision or the working tree into a throwaway copy of the project,
// so the build never touches the project open in the editor.
//
// node build.mjs --out <folder> [--revision <git ref>] [--webgl] [--unity <Unity.exe>]
//
// Without --revision the working tree is built, uncommitted changes included.
// The soak client sources in Assets/SoakTest always come from the working tree, so older revisions can be measured too.

import { execFileSync, spawnSync } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..", "..");
const options = parseArguments(process.argv.slice(2));
const outputDirectory = path.resolve(options.out);
const projectDirectory = path.join(outputDirectory, "Project");

fs.rmSync(projectDirectory, { recursive: true, force: true });
fs.mkdirSync(projectDirectory, { recursive: true });

if (options.revision) exportRevision(options.revision);
else copyWorkingTree();

copyDirectory(path.join(repositoryRoot, "Assets", "SoakTest"), path.join(projectDirectory, "Assets", "SoakTest"));
copyFile(path.join(repositoryRoot, "Assets", "SoakTest.meta"), path.join(projectDirectory, "Assets", "SoakTest.meta"));
removeEditorOnlyPackages();

const unityPath = options.unity ?? findUnity();
build("BuildWindows", "Win64", path.join(outputDirectory, "Windows"));
if (options.webgl) build("BuildWebGL", "WebGL", path.join(outputDirectory, "WebGL"));
console.log(`soak client built into ${outputDirectory}`);

function exportRevision(revision) {
    const archivePath = path.join(outputDirectory, "source.tar");
    execFileSync("git", ["-C", repositoryRoot, "archive", "--format=tar", "-o", archivePath, revision]);
    // A relative path, because some tar builds read "C:\..." as a remote host.
    execFileSync("tar", ["-xf", path.relative(projectDirectory, archivePath)], { cwd: projectDirectory });
    fs.rmSync(archivePath);
}

function copyWorkingTree() {
    const files = execFileSync("git", ["-C", repositoryRoot, "ls-files", "--cached", "--others", "--exclude-standard", "-z"], { encoding: "utf8", maxBuffer: 64 * 1024 * 1024 })
        .split("\0")
        .filter((file) => file && fs.existsSync(path.join(repositoryRoot, file)));
    for (const file of files) copyFile(path.join(repositoryRoot, file), path.join(projectDirectory, file));
}

// The build needs neither editor tooling packages nor network access for them.
function removeEditorOnlyPackages() {
    const manifestPath = path.join(projectDirectory, "Packages", "manifest.json");
    const manifest = JSON.parse(fs.readFileSync(manifestPath, "utf8"));
    delete manifest.dependencies["com.bananaparty.unislop"];
    fs.writeFileSync(manifestPath, JSON.stringify(manifest, null, 2));
    fs.rmSync(path.join(projectDirectory, "Packages", "packages-lock.json"), { force: true });
}

function build(method, buildTarget, output) {
    const logFile = path.join(outputDirectory, `build-${buildTarget}.log`);
    console.log(`building ${buildTarget}, log: ${logFile}`);
    const result = spawnSync(
        unityPath,
        [
            "-batchmode",
            "-quit",
            "-projectPath",
            projectDirectory,
            "-buildTarget",
            buildTarget,
            "-executeMethod",
            `BananaParty.WebSocketRelay.SoakTest.Editor.SoakBuild.${method}`,
            "-soakOutput",
            output,
            "-logFile",
            logFile,
        ],
        { stdio: "inherit" },
    );
    if (result.status !== 0) {
        console.error(`Unity exited with ${result.status}, see ${logFile}`);
        process.exit(result.status ?? 1);
    }
}

function findUnity() {
    const projectVersion = fs.readFileSync(path.join(repositoryRoot, "ProjectSettings", "ProjectVersion.txt"), "utf8");
    const version = /m_EditorVersion: (\S+)/.exec(projectVersion)[1];
    return path.join("C:", "Program Files", "Unity", "Hub", "Editor", version, "Editor", "Unity.exe");
}

function copyDirectory(source, destination) {
    fs.cpSync(source, destination, { recursive: true });
}

function copyFile(source, destination) {
    fs.mkdirSync(path.dirname(destination), { recursive: true });
    fs.copyFileSync(source, destination);
}

function parseArguments(argv) {
    const parsed = {};
    for (let index = 0; index < argv.length; index++) {
        if (!argv[index].startsWith("--")) continue;
        const key = argv[index].slice(2);
        parsed[key] = argv[index + 1] && !argv[index + 1].startsWith("--") ? argv[++index] : true;
    }
    if (!parsed.out) {
        console.error("usage: node build.mjs --out <folder> [--revision <git ref>] [--webgl]");
        process.exit(2);
    }
    return parsed;
}
