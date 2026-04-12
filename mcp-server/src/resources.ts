import { ResourceTemplate } from "@modelcontextprotocol/sdk/server/mcp.js";
import { godot, server } from "./server.js";

function toJsonResource(uri: string, data: unknown) {
  return {
    contents: [
      {
        uri,
        mimeType: "application/json",
        text: JSON.stringify(data, null, 2),
      },
    ],
  };
}

function hashData(data: unknown): string {
  return JSON.stringify(data);
}

function clampDepth(value: string | undefined, fallback = 10): number {
  const parsedValue = Number.parseInt(value || `${fallback}`, 10);
  if (Number.isNaN(parsedValue)) {
    return fallback;
  }

  return Math.max(1, Math.min(20, parsedValue));
}

function collectNodePaths(tree: unknown, result = new Set<string>()): Set<string> {
  if (!tree || typeof tree !== "object") {
    return result;
  }

  const record = tree as Record<string, unknown>;
  if (typeof record.path === "string" && record.path.length > 0) {
    result.add(record.path);
  }

  if (Array.isArray(record.children)) {
    for (const child of record.children) {
      collectNodePaths(child, result);
    }
  }

  return result;
}

async function getRuntimeStatus(): Promise<Record<string, unknown>> {
  return await godot.send("runtime", "get_bridge_status", {}) as Record<string, unknown>;
}

async function getRuntimeTree(depth = 10): Promise<Record<string, unknown>> {
  return await godot.send("runtime", "get_scene_tree", { depth }) as Record<string, unknown>;
}

const currentSceneResource = server.registerResource(
  "current-scene",
  "godot://scene/current",
  {
    mimeType: "application/json",
    description: "Metadata for the scene currently open in the Godot editor.",
  },
  async () => toJsonResource("godot://scene/current", await godot.send("scene", "get_current", {})),
);

const currentSceneTreeResource = server.registerResource(
  "current-scene-tree",
  "godot://scene/tree/current",
  {
    mimeType: "application/json",
    description: "Tree structure for the scene currently open in the Godot editor.",
  },
  async () => toJsonResource("godot://scene/tree/current", await godot.send("scene", "get_tree", {})),
);

const projectSettingsResource = server.registerResource(
  "project-settings",
  "godot://project/settings",
  {
    mimeType: "application/json",
    description: "Project settings visible from the current Godot editor session.",
  },
  async () => toJsonResource("godot://project/settings", await godot.send("project", "get_settings", {})),
);

const openFilesResource = server.registerResource(
  "open-files",
  "godot://editor/open-files",
  {
    mimeType: "application/json",
    description: "Files currently open in the Godot script editor.",
  },
  async () => toJsonResource("godot://editor/open-files", await godot.send("editor", "get_open_files", {})),
);

const recentErrorsResource = server.registerResource(
  "recent-errors",
  "godot://editor/errors/recent",
  {
    mimeType: "application/json",
    description: "Recent Godot MCP plugin errors, warnings, and bridge events captured in the editor.",
  },
  async () => toJsonResource("godot://editor/errors/recent", await godot.send("editor", "get_errors", { count: 100 })),
);

const compilationErrorsResource = server.registerResource(
  "compilation-errors",
  "godot://editor/compilation-errors",
  {
    mimeType: "application/json",
    description: "Structured compilation diagnostics parsed from recent Godot editor and bridge logs.",
  },
  async () => toJsonResource("godot://editor/compilation-errors", await godot.send("editor", "get_compilation_errors", { count: 200 })),
);

const runtimeStatusResource = server.registerResource(
  "runtime-status",
  "godot://runtime/status",
  {
    mimeType: "application/json",
    description: "Current runtime bridge connectivity and live scene identity for the running Godot game.",
  },
  async () => toJsonResource("godot://runtime/status", await getRuntimeStatus()),
);

const runtimeSceneTreeResource = server.registerResource(
  "runtime-scene-tree-current",
  "godot://runtime/scene/tree/current",
  {
    mimeType: "application/json",
    description: "Live runtime scene tree for the running Godot game (default depth: 10).",
  },
  async () => toJsonResource("godot://runtime/scene/tree/current", await getRuntimeTree(10)),
);

const runtimeFrameResource = server.registerResource(
  "runtime-frame-latest",
  "godot://runtime/frame/latest",
  {
    mimeType: "application/json",
    description: "Latest runtime performance counters from the running Godot game.",
  },
  async () => toJsonResource("godot://runtime/frame/latest", await godot.send("runtime", "capture_frame", {})),
);

const runtimeLogsResource = server.registerResource(
  "runtime-logs-recent",
  "godot://runtime/logs/recent",
  {
    mimeType: "application/json",
    description: "Recent runtime bridge events and runtime-side error logs captured from the running game.",
  },
  async () => toJsonResource("godot://runtime/logs/recent", await godot.send("runtime", "get_logs", { count: 100 })),
);

const runtimeSceneTreeDepthTemplate = server.registerResource(
  "runtime-scene-tree-depth",
  new ResourceTemplate("godot://runtime/scene/tree/{depth}", {
    list: undefined,
    complete: {
      depth: async (value) => ["3", "5", "10", "15", "20"].filter((option) => option.startsWith(value)),
    },
  }),
  {
    mimeType: "application/json",
    description: "Live runtime scene tree at a requested depth.",
  },
  async (_uri, variables) => {
    const depth = clampDepth(typeof variables.depth === "string" ? variables.depth : undefined);
    return toJsonResource(`godot://runtime/scene/tree/${depth}`, await getRuntimeTree(depth));
  },
);

const runtimeNodeTemplate = server.registerResource(
  "runtime-node-properties",
  new ResourceTemplate("godot://runtime/node/{nodePath}", {
    list: undefined,
    complete: {
      nodePath: async (value) => {
        try {
          const runtimeTree = await getRuntimeTree(8);
          const candidates = Array.from(collectNodePaths(runtimeTree.tree)).map((path) => encodeURIComponent(path));
          return candidates
            .filter((candidate) => candidate.toLowerCase().includes(value.toLowerCase()) || decodeURIComponent(candidate).toLowerCase().includes(value.toLowerCase()))
            .slice(0, 20);
        } catch {
          return [];
        }
      },
    },
  }),
  {
    mimeType: "application/json",
    description: "Live property snapshot for a runtime node. nodePath must be URI-encoded.",
  },
  async (_uri, variables) => {
    const encodedNodePath = typeof variables.nodePath === "string" ? variables.nodePath : "";
    const nodePath = decodeURIComponent(encodedNodePath);
    return toJsonResource(
      `godot://runtime/node/${encodeURIComponent(nodePath)}`,
      await godot.send("runtime", "get_node_properties", { node_path: nodePath }),
    );
  },
);

const runtimeToggleableResources = [
  runtimeSceneTreeResource,
  runtimeFrameResource,
  runtimeSceneTreeDepthTemplate,
  runtimeNodeTemplate,
];

for (const resource of runtimeToggleableResources) {
  resource.disable();
}

const watchedResources = [
  {
    uri: "godot://scene/current",
    fetcher: async () => godot.send("scene", "get_current", {}),
  },
  {
    uri: "godot://scene/tree/current",
    fetcher: async () => godot.send("scene", "get_tree", {}),
  },
  {
    uri: "godot://project/settings",
    fetcher: async () => godot.send("project", "get_settings", {}),
  },
  {
    uri: "godot://editor/open-files",
    fetcher: async () => godot.send("editor", "get_open_files", {}),
  },
  {
    uri: "godot://editor/errors/recent",
    fetcher: async () => godot.send("editor", "get_errors", { count: 100 }),
  },
  {
    uri: "godot://editor/compilation-errors",
    fetcher: async () => godot.send("editor", "get_compilation_errors", { count: 200 }),
  },
  {
    uri: "godot://runtime/status",
    fetcher: async () => getRuntimeStatus(),
  },
  {
    uri: "godot://runtime/scene/tree/current",
    fetcher: async () => getRuntimeTree(10),
    runtimeOnly: true,
  },
  {
    uri: "godot://runtime/frame/latest",
    fetcher: async () => godot.send("runtime", "capture_frame", {}),
    runtimeOnly: true,
  },
  {
    uri: "godot://runtime/logs/recent",
    fetcher: async () => godot.send("runtime", "get_logs", { count: 100 }),
  },
] as const;

const lastResourceHashes = new Map<string, string>();
let runtimeResourcesEnabled = false;

async function pollResourceUpdates() {
  if (!server.isConnected()) {
    return;
  }

  let runtimeStatus: Record<string, unknown> = {};
  try {
    runtimeStatus = await getRuntimeStatus();
  } catch (error) {
    runtimeStatus = {
      runtime_connected: false,
      error: error instanceof Error ? error.message : String(error),
    };
  }

  const runtimeConnected = runtimeStatus.runtime_connected === true;
  if (runtimeConnected !== runtimeResourcesEnabled) {
    runtimeResourcesEnabled = runtimeConnected;
    for (const resource of runtimeToggleableResources) {
      if (runtimeConnected) {
        resource.enable();
      } else {
        resource.disable();
      }
    }

    if (server.isConnected()) {
      server.sendResourceListChanged();
    }
  }

  for (const watchedResource of watchedResources) {
    if ("runtimeOnly" in watchedResource && watchedResource.runtimeOnly && !runtimeConnected) {
      lastResourceHashes.delete(watchedResource.uri);
      continue;
    }

    try {
      const data = watchedResource.uri === "godot://runtime/status"
        ? runtimeStatus
        : await watchedResource.fetcher();
      const nextHash = hashData(data);
      if (lastResourceHashes.get(watchedResource.uri) === nextHash) {
        continue;
      }

      lastResourceHashes.set(watchedResource.uri, nextHash);
      await server.server.sendResourceUpdated({ uri: watchedResource.uri });
    } catch (error) {
      const failureHash = `error:${error instanceof Error ? error.message : String(error)}`;
      if (lastResourceHashes.get(watchedResource.uri) === failureHash) {
        continue;
      }

      lastResourceHashes.set(watchedResource.uri, failureHash);
      await server.server.sendResourceUpdated({ uri: watchedResource.uri });
    }
  }
}

const resourcePoller = setInterval(() => {
  void pollResourceUpdates();
}, 2500);

resourcePoller.unref?.();

void currentSceneResource;
void currentSceneTreeResource;
void projectSettingsResource;
void openFilesResource;
void recentErrorsResource;
void compilationErrorsResource;
void runtimeStatusResource;
void runtimeLogsResource;
