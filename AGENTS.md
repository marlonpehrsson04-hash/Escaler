<!-- This file is automatically updated by ikon tool commands. User edits are preserved only below the ikon-user-content-below marker. -->

# Ikon AI App Development Guidelines

**Detailed API references are in docs/Ikon.Agent.Docs/guides/. See the guide index at the bottom of this document.**

## Architecture

An Ikon AI App is a C# app that runs in the cloud and streams reactive UI to browser clients over WebSocket. It keeps running when every client disconnects, and many clients share one app instance (collaboration, games, shared experiences). Nearly all development, styling included, happens in the C# app.

**AI services** — the platform holds every API key; no setup:
- `Emerge.Run<T>()` — LLM text and structured output (Claude, GPT, Gemini, Grok, …)
- `ImageGenerator.GenerateAsync(prompt)`, `SpeechGenerator.GenerateAsync(text)`, `SpeechRecognizer.RecognizeAsync(samples, sampleRate)`, `VideoGenerator.GenerateAsync(prompt)`, `EmbeddingGenerator.EmbedAsync(texts)`, `WebSearcher.SearchAsync(query)`

Each service has a static one-shot like these with a sensible default model; `new Xxx(model)` plus its `XxxConfig` is for advanced options (batching, input images, sizes, timeouts).

**UI:** declared in C# inside `UI.Root([Page.Default], content: view => { ... })`. Components are methods on `view` (`UIView`; `IView` is a global alias for it, so a helper may take either — there is no `ViewContext`, CS0246). Nested content lambdas get their own `UIView view`.

**State:** `Reactive<T>` (shared by all clients), `ClientReactive<T>` (per client), `UserReactive<T>` (per user); collections are `ReactiveList<T>` and friends. Changes re-render automatically and only the diff is streamed.

**Styling:** Crosswind — Tailwind-like classes in C# string arrays, plus `motion-[...]` keyframe animations. Three layers compose in one array (`[Button.PrimaryMd, "mt-4 self-center"]`):

1. **Semantic theme classes** (`bg-card`, `text-primary`, `bg-brand-solid`, `border-secondary`, `bg-background`) — the default. They follow light/dark and per-app `IkonTheme` overrides.
2. **`Ikon.Parallax.Theming` tokens** (`Button.PrimaryMd`, `Card.Default`, `Text.H1`, `Layout.Page`, …) — pre-composed bundles of layer 1; use them when they fit.
3. **Fixed palette classes** (`bg-amber-400`, `text-zinc-950`) — for looks that deliberately ignore the theme; every such site must be revisited for a later re-skin or dark mode. A colour the palette lacks goes in the theme as a role, not as a hex class (IKON013).

**A `style:` array is exactly what renders.** With no array a component gets its themed default (`view.Button` → `Button.Default`, `view.TextField` → `Input.Default`); with one, it gets only those classes — `view.Button(["px-6"], …)` is a bare button. Put `"default"` first to keep the themed default underneath, your classes winning: `view.Button(["default", "px-6"], …)`. Token composites (`Button.OutlineMd`, `Card.Interactive`, `Badge.SuccessMd`) are complete styles: pass them as-is, add classes after, never put `"default"` before one. `"unstyled"` is a no-op. Slot styles with themed defaults (`contentStyle:` on Popover/Tooltip/HoverCard; Dialog's `titleStyle:`/`descriptionStyle:`/`headerStyle:`) follow the same rule.

**Theming:** `new IkonTheme { ["primary"] = "amber-400", ["background"] = "zinc-950", … }` at the `UI` initializer. Each entry sets a CSS variable; a vocabulary alias (`primary`, `card`, `radius`, `density`, …) sets its documented cluster. The only named properties are `Mode` and the scheme pairs `DarkMode` (or `Dark`) and `Light`, each a whole `IkonTheme` — there is no `Brand =`/`Background =`, and no local `IkonTheme.cs`. The `theming` guide is the reference.

**Audio and video:** `private Audio Audio { get; } = new(app);` — synthesis (Ikon.Resonance), effects, playback, microphone capture. Camera/screen capture is `private Video Video { get; } = new(app);`.

**Namespaces:** the common Ikon namespaces are imported by `GlobalUsings.cs` (exceptions under "App structure and platform").

**Frontend:** touch `frontend-node` only to integrate custom React components. Any NuGet package (C# app) or npm package (frontend) may be added.

## Running and checking the app

Run it from the app root with `ikon run` (add `--log-debug` for the server's own detail); the
URLs land in `build/app/artifacts/bin/<App>/debug/ikon-server-info.json`. Then check it as text
before reaching for a browser:

```bash
ikon browse --steps "observe"                                   # screen text + [id] action handles
ikon browse --steps "fill 1 hello; tap Send; wait 1500; look after.png"
ikon browse --text-only --format json < steps.txt               # one step per line, one JSON object per step
```

`observe` prints what the screen says and which actions exist; `tap <id|label>` and
`fill <id|label> <text>` act through the SDK and print only what changed — a field's label of
several words goes in quotes (`fill "Name for the cup" Aino`); `look [file.png]`
screenshots a headless browser that has joined the **same client session**, so the pixels always
match the text. `click <selector>`, `eval <js>` and `console` reach the DOM for custom React
components. `expect <text>` and `expect-absent <text>` pass or fail on what the screen shows (visible
text and control labels, case-insensitive, waiting a few seconds for it to settle), and a run with a
failed step exits non-zero. An observation costs a few hundred tokens where a screenshot costs
thousands — read the text first and screenshot only when the question is about pixels.
`ikon stop` ends the run.

A script worth keeping becomes a **check gate**: `.ikon/gates/<name>.toml` (the name in lower-case
letters, digits and `-`) with `kind = "check"`, an optional `description`, `blocking = false` for one
that reports without failing the run (except under `--regression`, where every failed gate fails it), and the script in `steps`, one step per line (`#` starts a
comment). A gate that creates data leaves none behind for the next run to trip over.
`ikon test` runs every gate against the local run or, with `--target`/`--app-id`,
the deployed app; `ikon test <name>` runs one. The gate that shows a defect is the change's
reproduction: `ikon task set <task-id> --reproduction <name>` attaches it to a proposed change,
accepting the change binds the file, and `ikon test --regression` runs the accepted ones —
refusing any whose file has changed since.

The same steps drive the **deployed** app, signed in as you with your `ikon login` — no browser
sign-in: `ikon browse --app-id <space> --steps "observe"` (or `--target <name>`). With neither,
a local run is browsed when one is up, and otherwise the cloud app the project is linked to. `look` then
opens the app's own address and joins that same signed-in session. An app with no active deployment
is refused rather than started.

## The ikon tool's commands

This part of AGENTS.md comes from the ikon tool and is rewritten whenever the tool or the app's
Ikon libraries change. The tool's own help is the authority on its commands, ahead of any guide:

- `ikon help <section> --json` — one section's commands with their options, positionals and
  remarks; the sections are Develop, Source, Ship, Services, Operate, Autopilot, Account and Machine
- `ikon help <command> --json` — one command; `ikon <command> --help` prints the same for a person
- `ikon help --json` — every command, and what holds for all of them: how the app and the platform
  environment are chosen, and the shape of `--format json` output and failures

A command a doc names that the tool rejects as unknown has been renamed — look it up there rather
than guessing. Each setting in `ikon-config.toml` is explained by the comment above it in the file.

## What the app must contain

Before writing UI, name the KIND of product and what its ordinary workflow needs. Users miss a standard control faster than a missing feature, and being asked for one after the first build is the failure this section prevents. Convention decides, not the request:

| The app is… | It needs, from the first build |
|---|---|
| records people accumulate (contacts, invoices, tasks, recipes) | search · sort · filters on the high-value fields · a date range when time changes which records matter · export where taking data out is a real task |
| messaging or comments | reply · copy · edit and delete for the author · a per-message action row that works on touch |
| a board or pipeline | move by drag AND a non-drag path · a card detail |
| a dashboard | the summary numbers first · a chart matched to the question · drill-down to the rows |
| an editor or canvas | undo/redo · select · delete · duplicate · zoom by wheel and pinch where the surface scales |
| media playback | play/pause · seek · elapsed and total time |
| a queue someone reviews | the review verbs — Approve · Reject · Request changes, never a bare Yes/No · the evidence to decide, in one place |
| a builder of multi-step automations | one stated trigger · labelled branches with a default path · per-step test · a run log |

This is the minimum for the workflow asked for, **not a feature list**: anything unrequested that the workflow runs fine without stays out. Three controls a user would miss beat ten that pad the screen.

- **Reversal is not an editor's privilege.** Anything that changes user-visible state undoes — AI-made changes included — or says before the act that it cannot. A time-bounded Undo beats a confirmation dialog; keep the dialog for the genuinely irreversible.
- **Only build a control that works.** A visible control works, is disabled with a visible reason, or does not exist — no Filter chip that filters nothing, no Export with an empty handler, no Save button in an app that saves on every change. When the user's own permission blocks it, say so and offer Request access if the app can send one.
- **A control that narrows a collection must not lose data.** Derive filter and grouping options from the values present, show an option with no items as empty with a count, and make Clear return the whole collection. This failure is silent.
- **Reconcile before you add.** On a later pass, repair an existing equivalent control in place, keeping its value, bindings and permissions; never build a second one beside it. Remove the old one only once the replacement works.
- **Completeness is not clutter.** Frequent controls visible, selection-specific ones contextual, the rest in a menu. On a narrow viewport they move into a compact surface; they do not vanish.
- **Separate the surfaces.** Authoring affordances (drag handles, resize grips, edit controls) stay out of the reading surface, and interactive is not editable — a reader may filter a chart without the right to change it.
- **Consequential actions need a human act.** Anything reaching outside the app — `app.Payments`, `app.Email`, `app.Telephony`, `app.Notifications`, a publish, a permanent delete — happens when a person presses something naming the action, never as a side effect of an AI turn. The LLM may draft, price or pick; a person confirms. Reversible in-app work (generate, draft, preview, rearrange) asks for no confirmation.

Recipes are in the pattern corpus: `record-list-toolbar`, `message-action-row`, `board-move-without-drag`, `form-field-discipline`, `chart-for-the-question`, `zero-results-state`, `overlay-selection`.

## How the app must look

Every app ships polished on the first build, unasked — the user judges the first screenshot before any code. Never present a bare default and offer polish as a follow-up.

**Pick a visual direction before the first `view.*` call**, in one sentence: the product's feel (calm finance tool, loud party game, dense operator console, warm journal), palette family, type character, corner radius, density. Commit it in the app constructor with `new IkonTheme { … }` — `["primary"]` (IKON015 flags a theme without it), background mood, font, radius. The platform default is not a finish; two apps from different requests must not look alike.

Execute it everywhere:

- **Hierarchy.** One primary action per screen in the brand colour (`Button.PrimaryMd`), the rest neutral, outline or ghost. One display headline (`Text.Display`/`Text.H1`), then a real type scale down to `Text.Caption`.
- **Surfaces with depth.** `Card.Elevated` for what matters, `Card.Subtle`/`Card.Flat` for the rest, `Card.Glass` over an image. A page of identical `Card.Default` boxes reads as unfinished. One flat surface colour per card — a gradient reads as a template (IKON011).
- **Space and rhythm.** Generous padding (`p-6`/`p-8`), a consistent gap scale (`Layout.Column.Lg` between sections, `Layout.Column.Sm` within a group), a max-width page (`Layout.Page`) so text never runs edge to edge.
- **A backdrop, not a void.** A tinted background, a hero image or an accent panel; tinted icon tiles (`bg-brand-solid/10 rounded-lg`) beside stat values and list rows.
- **Motion where state changes.** Fade/slide in new content (`motion-[…]`), hover and press feedback on everything clickable (`State.Pressable`, `Transition.Fast`), skeletons while loading, a spinner on a busy button; nothing animated for decoration.
- **Designed empty, loading and error states** (`EmptyState.*`, `Skeleton.*`, `Alert.*`) — never a blank panel or a raw exception string.
- **Every viewport.** On narrow layouts rows wrap or stack, touch targets are ≥ 40px, nothing overflows. The app fits the viewport: an `h-screen` root, and a `ScrollArea` inside a bounded parent (`flex-1 min-h-0`) for anything that grows — never a page the browser scrolls (`app-structure` guide, "Viewport Layout").
- **Theme-safe.** Semantic classes and tokens carry the direction across light and dark; fixed palette classes only for deliberate fixed-brand surfaces (see "Color tokens and theming"). Small text stays at AA contrast.

The `crosswind-reference` and `theming` guides carry the recipes (glass, icon containers, hero layouts, motion).

## Common Pitfalls

Each of these compiles or runs in the wrong shape. The two most common draft compile failures:
1. Collection state uses the reactive collections — `ReactiveList<T>`, `ReactiveDictionary<TKey,TValue>`, `ReactiveHashSet<T>` (+ `Client*`/`User*`/`Mount*`/`Persistent*`/`PersistentUser*` variants). A `Reactive<T>` wrapping a mutable collection (`Reactive<List<T>>`) is IKON002, an error in the codegen build and in-repo apps.
2. There is NO `view.Keyed(...)` (CS1061). Render list items directly in the loop; the diff tracks identity. `key:` is a named parameter on the component, needed only where a stateful component must keep or drop its state.

### Reactive state

- **Re-render is implicit.** Reading `_x.Value` in a UI lambda registers the dependency. There is NO `view.Dynamic`/`Watch`/`Bind`/`Observe`/`When`/`Live`/`Reactive` (CS1061); `UIView` only renders components.
- **Reactives are `private readonly` fields constructed with the initial value**: `private readonly ClientReactive<int> _count = new(0);`, `new("")`, `new(new())`. No `app` argument, no `app.ClientReactive(...)` (CS1503/CS7036). A `ClientReactive` hands every client that one initial instance — for a mutable type use `ClientReactive.Create(_ => new Settings())`. Collections start empty with `= new();`; seeding a `Client*`/`User*` collection with items shares those instances too, so mutable items take the per-scope factory overload.
- **Mutate collections on the reactive itself**: `_todos.Add(item)`, `.Remove`, `.RemoveAll`, `.RemoveAt`, `.Insert`, `.Clear`, `.ReplaceAll`, `.Sort`, `.AddRange`, `_todos.Update(list => …)`; `_map[key] = v`, `.Remove(key)`. Each notifies once. Enumerate it directly (`foreach (var t in _todos)`; `.Count`, `[i]`, `.ContainsKey` are tracked). `.Value` is read-only (`.Value.Add` is CS1061); `.Value = newList` replaces the content like `ReplaceAll`.
- **Typed text and drafts are USER scope.** A reload is a new client session, so a `ClientReactive` draft vanishes; `UserReactive<string>` (or `PersistentUserReactive`) survives reloads and follows the user across tabs. Never patch this with browser storage.
- **Per-client state is implicit inside a scope.** In UI lambdas and handlers read/write `_x.Value` — no client-id lookup, no `_x.Get(clientId)`. Client/session ids are `int`. Outside a scope (a loop started from `Main`, a timer, an endpoint handler) or when writing to *another* client, name the target: `_x.SetFor(clientSessionId, value)` / `_x.ValueFor(clientSessionId)` (`UserReactive` takes the `string` userId, `MountReactive` the mountId; lists have `AddFor`/`RemoveFor`/`ClearFor`/`UpdateFor`, dictionaries `SetFor(id, key, value)`/`ClearFor`). Capture the id while the scope exists (`var cid = ReactiveScope.ClientId;`, or `ctx.ClientSessionId`); a `Task.Run` started in a scoped callback inherits the scope. A scopeless `.Value` throws with the fix in its message. For a region of several reads and writes: `using (ReactiveScope.Use(new ClientScope(cid))) { … }` (an `IScopeKey`, never the bare int). Plain `Reactive<T>` needs none of this.
- **Mount-scoped state is the `Mount*` family** (`MountReactive<T>`, `MountReactiveList<T>`, `MountReactiveDictionary<TKey,TValue>`, `MountReactiveHashSet<T>`) — one value per Parallax mount, for an app that embeds an `aiCanvas` beside its `ikon-ui` page. Across one client's mounts use `ClientReactive<T>`; across all clients, `Reactive<T>`.
- **Render only reads.** A reactive written inside a render lambda is not propagated (the platform logs `update was ignored because it was done within a reactive callback`). Create-if-missing goes in the event handler or a lifecycle callback, never in the `UI.Root` lambda.
- **A side effect that follows state is `ReactiveEffect`**: `new ReactiveEffect(async ct => …, _a, _b)` re-runs when a dep changes and cancels the token when one changes mid-run; keep it in a field, `Dispose()` to stop. `ClientReactiveEffect`, `UserReactiveEffect` and `MountReactiveEffect` run one per scope and do NOT fire at construction — the first dep change in that scope starts it.
- **`Cells` is advanced** — only when the plan needs keyed shared sessions across isolated instances; otherwise `Reactive<T>`/`PersistentReactive<T>`. It needs `using Ikon.App.Cells;` and is reached through `Cells.Instance` (`Cells.Instance.Connect<IFoo>(new SessionIdentity(key))` returns the interface; a bare `Cells.Connect` is CS0120). A `[Cell]` class takes an `ICell<TSessionIdentity>` primary-constructor parameter carrying its `Identity`. On a cloud run a reactive read through a cell interface is a `MirrorReactive<T>`: pattern-match it and render its `Status` (`Connecting`/`Live`/`Failed`) and `Error`, never the seed as an answer. `AppServices.Instance` gives cells the session's `Secrets`, `DatabaseAsync`, `OpenDatabaseAsync` and `WhenReadyAsync` in every app session; its `HostApp` is null outside cell-host mode.

### AI services

- **`Emerge` is static** — no `IEmerge`, no DI.
- **`await Emerge.Run<T>(…)` is the one-shot — no terminal call.** Copy whole: `var result = await Emerge.Run<MyResult>(LLMModel.Claude46Sonnet, pass => { pass.Command = userPrompt; });`. The prompt goes in `pass.Command`, never positionally (CS1660); the result is a non-null `T`, not a tuple (CS8130). Seed input (images, prior turns) with a `KernelContext` second: `Emerge.Run<T>(model, ctx, pass => …)` — a `MessageBlock` of `TextPart`, `ImagePart`/`ImageUrlPart`, `PdfPart`/`PdfUrlPart`, `AudioPart`, `VideoPart`/`VideoUrlPart`/`VideoAssetPart` (`*UrlPart` by url, not bytes; `AudioIdPart` replays earlier model audio by provider id). `.FinalAsync()` only when you need the updated context: `(T? Result, KernelContext Context)`, still nullable. Streaming: `await foreach (var ev in Emerge.Run<T>(…))`. A run is awaited OR enumerated: awaiting twice is fine, awaiting after enumerating to Completed returns that result (after Stopped, or Completed with no result, it throws `EmergenceStoppedException` like a plain await); any other mix, or enumerating twice, throws `InvalidOperationException` — start a new run.
- **`Emerge.AskAsync` is prompt-first** — `AskAsync(userPrompt)` or `AskAsync(userPrompt, LLMModel.Claude45Haiku)` (model-first is CS1503). It returns a `string`; `AskAsync<T>(prompt)` returns a `T` (a `public record`/`class`, never a `struct`) — the one-call typed form. Use `Run<T>` when you need the pass's controls.
- **Models.** `LLMModel.Default` is `Claude45Haiku`, what the one-shots run on — pass it when you have not decided. Name a stronger one when the task warrants (`Claude46Sonnet` for reasoning and chat); the `ai-models` guide lists them.
- **AI calls never return null — catch instead of guarding.** Every one-shot (`ImageGenerator.GenerateAsync`, `SpeechGenerator.GenerateAsync`, `VideoGenerator.GenerateAsync`, `MusicGenerator.GenerateAsync`, `WebSearcher.SearchAsync`, …) throws on failure, the standalone services an `AIException`. Emerge (`Run<T>`, `AskAsync`) throws `EmergenceStoppedException` — not an `AIException` — when a run ends without a result, and otherwise rethrows the generation failure (an `AIException`, local or remote; a `FunctionCallException` for a remote failure that is not an AI error, such as a policy denial). So `catch (AIException)` around the services, `catch (Exception)` around Emerge. Use `?.`/`??` when rendering from state.
- **The catch renders a human sentence and a retry, never the exception.** A cloud AI call can fail at runtime on a perfect build, in front of a non-developer. Show `"Couldn't create the image — try again."` beside a retry button; never `ex.Message`, `ex.ToString()`, a provider/model id or a socket/HTTP error. Every surface showing a generated result has three states: waiting, failed-with-retry, empty. **A failure keeps the work that produced it** — prompt, upload, form input and earlier output survive, so Retry is press-again; when one part of a multi-part generation fails, retry only that part.
- **`WebSearcher.SearchAsync(q, maxResults: 5)`** works static and on an instance. `SearchPagesAsync(new SearchConfig { ... })` targets sites, countries and languages; `SearchImagesAsync` finds images. `SearchResult` has `Url`, `Title`, `Content` (no `Snippet`).

### The view-call shape

- **The leading `string[]` style array is positional; name everything else.** `view.Column(["flex gap-4"], content: v => …)`, `view.Text(["text-lg"], text: "Hi")`, `view.Button(["px-4 py-2"], text: "Save", onClick: async () => …)`. Never also write `style:` (CS1744), never a named arg before a positional (CS8323). The display string is `text:` on Text, Button, Link and ActionButton (not `value:`/`label:`); with a `content:` lambda on Button, Link or ActionButton, `text:` becomes the aria-label. Content-first overloads also exist — `view.Text("Hi")`, `view.Button("Save", onClick: …)`, `view.Heading("…")`, `view.Markdown("# hi")`, `view.Icon("check")` — but lambdas are always named (IKON003).
- **`bind:` is the two-way form for form controls**: TextField/TextArea/Select/RadioGroup bind `Reactive<string>`, Checkbox/Switch `Reactive<bool>`, Slider `Reactive<double>`. Without a reactive use `value:` + `onValueChange:`; if both are passed, `bind:` wins and `onValueChange:` still fires. Manual pairs: Select/RadioGroup `value:` is a string, Checkbox/Switch/Toggle a bool. A `value:` with neither (and on TextField/TextArea no `onSubmit:`) renders read-only. `formValue:` (the HTML form attribute) is Checkbox/Switch only; Toggle takes neither it nor `bind:`. `view.Select` options are `new SelectOption(value, label)`.
- **Slider has two overloads.** On the style-first one `value:` is a LIST of thumbs — `view.Slider([Slider.Default], value: [50.0], onValueChange: async v => …)` (`bind:` lives here). The scalar one is value-first: `view.Slider(50, style: [Slider.Default], onValueChange: async v => …)`.
- **TextField `disabled:` can be reactive** — flipping it mid-typing keeps focus. Gate async work with a disabled input, Button, or both. TextField/TextArea take `debounceMs:`.
- **Handlers.** `onSubmit:` is `Func<string, Task>` — always `async`. `onClick:` takes any parameterless lambda. Only `Box`, `Button`, `Card`, `Image`, `Link`, `TableRow` and `ToolbarButton` take `onClick:` (charts take one with `ChartClickArgs`); Text and layout primitives do not. A clickable `view.Box` gets button semantics; give an icon-only one `ariaLabel: "Remove"`. `onOpenChange` hands a plain `bool`.
- **`props:` needs `new Dictionary<string, object> { … }`**, not target-typed `new()`. Column/Row/Grid take no `props:`.
- **The style array is classes, not CSS** — there is no `inlineStyle:`. A runtime value goes in as an arbitrary value: `$"w-[{pct:0.#}%]"`, `$"left-[{x:0.#}%]"`, `$"h-[{n}px]"`. It is the only form every renderer honours; an inline `["style"]` prop is web-only and does nothing on native clients.
- **Lambda parameters can't be reserved words** (`checked`, `event`, `params`, `lock`, `default`, `base`, `new`, `object`, `string`) — name it `isChecked`.
- **A ternary of two style arrays needs a target type** (CS0173 on `var`): a `string[]` local, the ternary passed straight into the call, or a conditional element: `["rounded p-2", isActive ? "bg-active" : "bg-card"]`. CS1003/CS1525/CS1026 near a style array is an unescaped quote, unbalanced brace or reserved-word parameter instead.
- **Component parameters.** Dialog: `title:`/`description:`/`content:` + `titleStyle:`/`descriptionStyle:`/`headerStyle:`. AlertDialog: the same plus `cancelLabel:`/`actionLabel:`/`onAction:`, slot styles `titleStyle:`/`descriptionStyle:`/`footerStyle:`/`cancelStyle:`/`actionStyle:` — no `headerStyle:`. The field family's `label:` renders a VISIBLE label above the control — omit it when the design has none (`placeholder:` is the handle); on Checkbox/Switch/Toggle it is the clickable trailing text.
- **Size constants.** `view.Icon(name: "check", size: IconSize.Lg)` (`size:` like Spinner; an `[Icon.Sm]` token wins when both are given). Size ENUMS never go in a style array (CS0029) — use the class constant (`Icon.Sm`, not `IconSize.Sm`). Buttons bake variant and size into one constant: `Button.PrimaryMd`, `Button.ErrorMd` (= `Button.DestructiveMd`), `Button.OutlineErrorMd`, `Button.GhostErrorMd` — no `Button.Danger*`, no `variant:`/`size:` params. Text: `Text.Body`, `Text.Caption`, `Text.H1`…, `Text.Sm`/`Text.Md`/`Text.Lg`; there is no `Text.Xs`.
- **`view.Spinner()`** is built in (`size: SpinnerSize.Lg`; a style array recolours it).
- **`ScrollArea`**: the style array is the outer wrapper, `viewportStyle:` the scrolling area. `autoScrollKey:` takes what changes — the collection itself, a count, or a composite string.
- **`view.Image(["w-72"], src: url, alt: "…")`** or `(…, data: bytes, mimeType: "image/png")` — source named.
- **Copy, share and download are `view.ActionButton`**: `view.ActionButton([…], action: ActionKind.CopyToClipboard, options: new CopyToClipboardActionOptions { Text = theText }, content: …)`; likewise `ActionKind.Share` and `ActionKind.DownloadFile` with `DownloadFileActionOptions { Filename = "notes.md", Data = bytes }`. There is no `ClientFunctions.CopyToClipboardAsync`/`RunJavaScriptAsync`; the one server-initiated download is `ClientFunctions.DownloadFileAsync(url, filename)` (false when the client cannot). Notifications go through `app.Notifications`.
- **Interactive elements need findable handles** — `label:`/`placeholder:` on fields, a clear label on buttons, `key:` on dynamic rows; assistive tech and the app validator depend on them. Pair a slider whose value matters with labelled +/- buttons.
- **`hover:` never fires on touch.** Hover-revealed controls (`opacity-0 group-hover:opacity-100`) also need `pointer-coarse:opacity-100` or `focus-within:opacity-100`.

### Custom frontend and real-time

- **Native `view.*` first; when Parallax lacks the piece, build it.** For a rich text or code editor, a map, a 3D view, a chart library, a timeline or a signature pad, never ship a worse approximation or drop the feature: read the `frontend-fundamentals` guide and the `custom-react-node-embed` pattern, add the npm package, and write all four parts in one change — React component, resolver, `registerModule` in `app.tsx`, C# extension method. A missing client part shows a red "Unregistered node type" placeholder, and the console error names all four. A static chart needing no interaction can instead be rendered server-side into `view.Image([…], data: bytes, mimeType: "image/svg+xml")`.
- **Real-time MULTI-USER surfaces** (shared whiteboard, per-frame multiplayer game) are the exception: a custom canvas plus a `schema/<Name>.tp` message, because `view.*` has no client→client channel and per-frame `Reactive` diffs flood it. The server routes (`app.OnMessage<T>(…)`, `app.SendMessageAsync(payload, targets)`), the canvas mounts once via `AddNode`, per-tick state flows over the `.tp` message, and lobby/score/controls stay in `view.*`. Follow the `multi-user-game` pattern.
- **Real-time frontend wiring**: `appMessaging` from `@ikonai/sdk`; renderer types (`UiComponentRendererProps`, `useUiNode`, …) from `@ikonai/sdk-react-ui` (there is no `@ikonai/sdk-react`). Inside a node component take the client from renderer props (`context.client`) — `useIkonApp()` there throws, and `useIkonApp({})` opens a SECOND connection. `.on()`/`.send()` take the generated codec's `AppMessageType<T>` descriptor, never an opcode name; unsubscribe with `sub.close()`. Use the typed server API, never a blind `app.OnMessageReceived` re-broadcast.
- **Single-user drawing/annotation is `view.ImageEditorCanvas`** (pre-registered): `view.ImageEditorCanvas(style: […], src: url, brushColor: …, brushWidth: …, tool: ImageEditorTool.Brush, onSave: async args => …, onHistoryChange: async args => …)`. `tool:` is the enum (`Brush`, `Eraser`, `Text`, `Arrow`, `Region`, `Lasso`, `Line`, `Polygon`); undo/redo/save fire by bumping the `triggerUndo:`/`triggerRedo:`/`triggerSave:` ints.

### Audio, files, media

- **Audio is synthesized SERVER-side and played through `Audio`** — no browser Web Audio or client JS synthesis. Every sound is a playback: `Audio.Play(MediaTargets.Everyone, samples, sampleRate)` plays PCM `float[]` (by hand or `Ikon.Resonance.Synth`; or an `AudioClip`), `Audio.Speak(MediaTargets.Everyone, text)` speaks (optional `new SpeechOptions { Voice = …, Model = …, Instructions = …, Speed = … }` — ElevenLabs models, the default included, accept only speed 1.0), and `Audio.PlayLive(targets, sampleRate)` returns a `LiveAudioPlayback` to `await live.WriteAsync(buffer)` into (a synth, relayed model audio, hand-rolled `SpeechGenerator` chunks). All return an `AudioPlayback` at once; `await playback.Completion` waits for playout and yields an `AudioPlaybackOutcome` instead of throwing. A music file or long recording plays straight from its URL, decoded as it plays: `Audio.Play(MediaTargets.Everyone, new Uri(url), new PlayOptions { Slot = "music", Loop = true })` (WAV, MP3, Ogg Vorbis or Opus; `AudioClip.DecodeAsync(bytes, mime)` decodes a short file into a clip). A short fixed sound replayed often (pad hits, clicks, notifications) is `var hit = Audio.CreateSound(clip)` once and `Audio.Play(targets, hit)` per hit: a client that can cache it plays its own copy, without the streaming delay. `AudioChunk` has `Samples`/`SampleRate`/`ChannelCount`.
- **Each client hears every playback aimed at it, mixed — up to 64 at once, no streams to manage.** Past 64 its oldest non-looping clip or sound `Play` is cut with outcome `Evicted`, or the new playback is when none is left. How a new playback treats those already in its slot (a plain string) is `PlayOptions.Mode`: `Mix` (the `Play` default — drum hits and effects overlap), `Replace` (a crossfade; the `Speak` and `PlayLive` default, so a new line fades the previous one out) or `Queue`. `Play` with Replace or Queue needs a `Slot`: `new PlayOptions { Mode = AudioMixMode.Replace, Slot = "music", Loop = true }`. `Audio.Stop`/`Pause`/`Resume(targets, slot)` and `Audio.SetSlotVolume` act on what those clients hear — `Audio.Stop(MediaTargets.To(clientId), "speech")` is barge-in — and `PlayOptions.Duck = [new SlotDuck("music", 0.2f)]` lowers the music while a line is heard. Speech nobody is connected to hear is not generated. `Audio.Raw.SendFrameAsync(targets, streamId, …)` is the unmixed, unpaced escape hatch for engines that mix and pace themselves. `Video.SendFrameAsync` has no audience check and no broadcast default.
- **`FileUpload` `accept:` is a string array** — `accept: ["image/*", ".pdf"]`.
- **File upload: the start hook decides where the bytes land.** `FileUploadCompleteArgs` carries `LocalTempFilePath` and `AssetUri`, exactly one non-null — or neither when the start hook returned `DiscardData = true` to consume chunks in `onChunkReceived` (`FileUploadChunkArgs`: `UploadId`, `FileName`, `MimeType`, `Size`, `Data`, `BytesWritten`; copy `Data` if you keep it). `onUploadComplete` fires after the byte count and SHA-256 match. One upload's hooks run in order, off the message loop, within deadlines (15 s for PreStart/Start, 10 s per chunk, 50 s for Complete) — hand heavy work to a background task. Different uploads' hooks run concurrently, so shared state changes through `Update`, the collection methods or `Interlocked`, never read-then-write `.Value`.
  - **Asset storage — for anything kept, mandatory for media:** `onUploadStart: async args => new FileUploadResult { AssetUri = new AssetUri(AssetClass.CloudFilePublic, path, app.GlobalState.SpaceId) }` (or from `onUploadPreStart`). The bytes stream into storage and `onUploadComplete` gets `args.AssetUri`: `if (args.AssetUri is { } assetUri) { var bytes = await Asset.Instance.GetBytesAsync(assetUri); }`. Store it as `Reactive<AssetUri?>`.
  - **Local temp file — a small file consumed once:** no start hook, or `onUploadStart: async args => true`. Read `args.LocalTempFilePath`; the directory is deleted when the app stops.
  - Reading `args.AssetUri` with no start hook supplying one is a silent no-op — the `if` body never runs.
- **Never copy video/audio into the app container** (disk and RAM are far too small). Upload to asset storage as above and analyze through `(await Asset.Instance.GetMetadataAsync(uri)).Url`, a signed URL the preinstalled `ffprobe`/`ffmpeg` read over HTTP. Never `GetBytesAsync` a media asset.
- **App files: root `public/` (served by URL path) and `data/` (private); `app.Files` at runtime.** `view.Image(["w-full"], src: "/hero.png")` for a file in `public/`. `await app.Files.Data.ReadTextAsync("rules.md")` reads a seeded or runtime-written file; `await app.Files.Public.WriteBytesAsync($"thumbnails/{id}.png", bytes, "image/png")` then `app.Files.Public.GetUrlAsync(...)` stores and serves generated media, persisting across deploys. Never read through `AppContext.BaseDirectory` (in cloud it is the host's folder) — use `app.Files.Data`, `app.DataDirectory` or an embedded resource. `app.DataDirectory` is read-only in cloud. Never regenerate a file that already exists in `public/` or `data/`.

### App structure and platform

- **Keep the scaffold's shape**: `[App] public partial class MyApp(IApp<SessionIdentity, ClientParameters> app)` with `public record SessionIdentity(string? UserId)` / `public record ClientParameters(string Name)`.
- **Field initializers may use the primary-constructor `app`** (`private Audio Audio { get; } = new(app);`) but no other instance member (CS0236). Wire events (`app.OnClientJoined(...)`) in `Main()`.
- **`Main()` returns after `UI.Root(...)`.** No `await Task.Delay(Timeout.Infinite)` — a Main that never returns fails the boot at the start timeout; ongoing work belongs in callbacks or background tasks.
- **Seed demo content** for shared state (`Reactive`/`PersistentReactive`) in `app.OnStarting` when the store is empty; per-user state (`UserReactive`/`PersistentUserReactive`) in `app.OnClientJoined` when that user's store is empty — there is no user scope in `OnStarting`. Staging-only test rows go in `app.Seed(async () => …)`, which never runs on production (`databases` guide).
- **A value presented as unique is GENERATED.** Room, join and invite codes, share slugs and ids come from `Random.Shared` or `Guid` — a readable placeholder ships as the real thing (a party game shipped with join code `GAME`). Seeded identities are distinct, and demo rows look like real data, not `Item 1`.
- **Lifecycle**: `app.OnStarting(async () => …)`, `app.OnStopping`, `app.OnClientJoined(async ctx => …)` (or `(ctx, parameters)`), `app.OnClientLeft`, `app.OnMessageReceived(async msg => …)` — not `(sender, args)` (CS1593).
- **Open a database connection PER operation**: `await using var conn = await app.DatabaseAsync("mydb"); await conn.OpenAsync();` in each method (it arrives unopened), never a connection field. `CREATE TABLE IF NOT EXISTS` in `app.OnStarting`. Beyond a table or two prefer EF Core — a `DbContext` per operation, `MigrateAsync` in `OnStarting`. A database `"X"` needs `ikon db create --name X`; the built-in one needs nothing.
- **Don't invent a `using Ikon.*;`** (CS0234), but a nested namespace is not imported with its parent (CS0246/CS0103 on a real type). Outside `GlobalUsings.cs`: `Ikon.AI.Emergence.Tree`, `Ikon.AI.Emergence.Structured`, `Ikon.AI.ImageUpscaling`, `Ikon.AI.MusicGeneration`, `Ikon.AI.ImageSegmentation`, `Ikon.AI.DepthEstimation`, `Ikon.AI.MeshGeneration`, `Ikon.AI.Reranking`, `Ikon.AI.Retrieving`, `Ikon.AI.Database`, `Ikon.AI.Storage`, `Ikon.AI.Provenance`, `Ikon.AI.Utils`, `Ikon.App.Cells`, `Ikon.App.Cron`, `Ikon.App.Triggers` (TriggerContext), `Ikon.App.Mcp`, `Ikon.Common.Core.Protocol` (TriggerEventType), `Ikon.Common.Core.Email` (EmailSendRequest), `Ikon.Common.Assets` (storage registration such as `AddCloudFileStorageAsync`), `Ikon.Crosswind`, `Ikon.Sdk`. The connector and pipeline namespaces also need their package first — a `PackageReference` named like the namespace (`Ikon.Connectors`, `Ikon.Connectors.Google`, `Ikon.Connectors.Microsoft`, `Ikon.Connectors.Browser`, `Ikon.Pipeline`, `Ikon.Pipelines.Public`; `pipelines` guide).
- **The message author type is `Author`**, not `MessageAuthor`: `readonly record struct Author(AuthorKind Kind, string? Name = null)`. Compare `msg.Author == Author.User` or switch on `msg.Author.Kind` (`User`, `Agent`); `Author.Agent("researcher")` makes an agent. Text rides in `Parts`: `await storage.AppendMessageAsync(threadId, new Message(Author.User, [new Content.Text(reply)]));`.
- **Keep the entry point and the global namespace.** `using` directives precede the top-level `return await App.Run(args);` (CS1529); never delete that line (CS5001) or move it into a `Program.cs`. Never add a `namespace` to app files: inside `namespace Ikon.App.<Anything>`, `App` resolves to the `Ikon.App` namespace, and `App.Run`/`App<…>` spiral into CS0234/CS0305/CS8956. New partial files go beside the main one, namespace-free; shared `using`s go in `GlobalUsings.cs`.

### Color tokens and theming

- **`bg-primary` is NOT the brand fill** — it is the PAGE SURFACE (white in light, near-black in dark). Brand fill is `bg-brand-solid`, brand text `text-brand-secondary`, brand border `border-brand`; `text-primary` is the reading colour and `border-primary` the neutral hairline. A `bg-primary` button vanishes on one scheme (Crosswind logs a warning).
- **A hardcoded palette needs `Mode = ThemeMode.Fixed`.** Pinning `["background"]`/`["card"]`/`["foreground"]` on an Adaptive theme with no `DarkMode` leaves dark-preference clients with dark-scheme text on your light surfaces — unreadable, and invisible on a light-mode dev machine. One look → `ThemeMode.Fixed`; both schemes → provide `DarkMode`.
- **Semantic tokens are role names; a numeric shade derives from the role.** `primary-600`, `brand-100`, `error-700`, `success-50` (roles: primary/brand, accent, error/destructive/danger, success, warning, info) mix the role toward white below 500 and black above. Other names take no shade (`text-tertiary-600` is IKON006). Any semantic token works in any colour utility (`bg-muted-foreground/20`).
- **Author Tailwind v4 class names.** Crosswind matches stock Tailwind v4; v3 habits render heavier shadows and rounder corners:

  | v3 wrote | v4 writes | v3 wrote | v4 writes |
  |---|---|---|---|
  | `shadow-sm` | `shadow-xs` | `rounded-sm` (any corner form) | `rounded-xs` |
  | `shadow` | `shadow-sm` | `rounded` | `rounded-sm` |
  | `blur-sm` / `blur` | `blur-xs` / `blur-sm` | `drop-shadow-sm` / `drop-shadow` | `drop-shadow-xs` / `drop-shadow-sm` |
  | `backdrop-blur-sm` / `backdrop-blur` | `backdrop-blur-xs` / `backdrop-blur-sm` | `outline-none` | `outline-hidden` |
  | `ring` | `ring-3` | `bg-opacity-50` (removed) | `bg-black/50` slash syntax |

  When porting a v3-era codebase (Replit/Lovable/Base44/v0 exports) or copying source files in, translate every class as you write it, and verify by comparing computed styles side by side with the original. Shadow rungs are themable (`["shadow-lg"] = "0 2px 4px rgb(0 0 0 / 0.07)"`, at most two layers); `shadow-lg shadow-red-500` recolouring still works.
- **Keep small text at AA contrast (4.5:1).** On light surfaces, `text-muted-foreground` at reduced opacity (`/60`–`/80`) and mid-tone accents fail at 9–13px. Use full-strength muted for small labels and a darker shade of the accent (`text-brand-700`) for accent text, keeping the bright accent for borders, dots and fills; white-on-accent fills need the darker step too.

### C# string literals

- **ASCII `"` delimiters only** — a curly `“”` used as a delimiter is CS1056; inside a literal it is just a character.
- **Escape inner quotes in `$"…"`** (`$"Add card to \"{col}\""`) — one unescaped `"` cascades into CS1002/CS1010/CS1026 across the file; fix that quote, not the phantom lines. Many quotes → `$"""…"""`.
- **Raw-string `$` count matches brace nesting**: literal `{}` (JSON in prompts) plus interpolation → `$$"""…"""` with `{{x}}`; interpolation only → `$"""…"""`; static → `"""…"""`. A mismatch is CS9006/CS9007.

### Runtime blank-screen triage

**Build-clean but the page shows nothing** — check in order: (1) the root doesn't fill the viewport → `UI.Root([Page.Default], content: view => { view.Column(["h-screen w-full …"], …); })`; (2) white-on-white → commit a theme and paint on `bg-background`/`bg-card`; (3) a red "Unregistered node type" placeholder → register the custom module or use native components. Fix root, height and background before restyling cards.

## API Reference Guides

Topic guides live in `docs/Ikon.Agent.Docs/guides/`. Each covers one subject:

- **agent-stages-and-history** (`docs/Ikon.Agent.Docs/guides/agent-stages-and-history.md`): stage machine, IStageMachine, SkillSet, StageTransitionResult, FSM, agent stages, pass record, PassMessage, PassToolCall, ContextSnapshot, StreamedContent, journal, JournalEntry, JournalCodec, checkpoint, ThreadCheckpoint, Checkpoints, ThreadSnapshot, PlanSnapshot, AppSnapshot, resume, replay
- **agent-threads** (`docs/Ikon.Agent.Docs/guides/agent-threads.md`): agent, AgentThread, Orchestrator, Persona, AgentApp, AgentPlan, sub-agent, spawn, AgentCall, RunSubAgentAsync, AgentCallSpec, artifact, Artifact, attachment, budget, BudgetRemaining, ThreadOptions, user decision, ask the user, long-running agent, multi-turn agent
- **ai-advanced** (`docs/Ikon.Agent.Docs/guides/ai-advanced.md`): database AI, vector store, storage
- **ai-image** (`docs/Ikon.Agent.Docs/guides/ai-image.md`): image generation, ImageGenerator, AI image, photo, generate image, ImageGeneratorConfig, upscale, upscaling, super resolution, ImageUpscaler
- **ai-models** (`docs/Ikon.Agent.Docs/guides/ai-models.md`): LLM model, model selection, Claude, Gemini, GPT, Grok, model enum, KernelContext, AI connection
- **ai-speech-and-audio** (`docs/Ikon.Agent.Docs/guides/ai-speech-and-audio.md`): speech, TTS, STT, voice, transcribe, whisper, sound effect, SpeechGenerator, SpeechRecognizer, SoundEffectGenerator
- **ai-video** (`docs/Ikon.Agent.Docs/guides/ai-video.md`): video generation, video enhancement, AI video, VideoGenerator, VideoEnhancer, video playback, video player, play video, display video, inline video, VideoUrlPlayer, autoplay, poster, show a video
- **ai-web-and-data** (`docs/Ikon.Agent.Docs/guides/ai-web-and-data.md`): web search, scrape, crawl, classify, decide, route, triage, score, OCR, embedding, vector, retrieve, rerank, file convert, WebSearcher, EmbeddingGenerator, Decider, Jev
- **app-api-reference** (`docs/Ikon.Agent.Docs/guides/app-api-reference.md`): IApp, host services, server API, navigation, session, common utilities
- **app-structure** (`docs/Ikon.Agent.Docs/guides/app-structure.md`): app file structure, session identity, client parameters, partial class, global usings, lifecycle, host services, navigation, background work, client functions, client platform, operating system, user agent, download link, messages, minimal app template, viewport layout, auto-scroll, QR code, join URL, multi-user session, invite link
- **asset-system** (`docs/Ikon.Agent.Docs/guides/asset-system.md`): asset, cloud file, local file, cloud json, storage, metadata, URI, optimistic concurrency
- **audio-video** (`docs/Ikon.Agent.Docs/guides/audio-video.md`): audio, video, capture, speech, stream, effects, reverb, delay, mixer, synthesizer, oscillator, filter
- **charts** (`docs/Ikon.Agent.Docs/guides/charts.md`): chart, charts, pie chart, bar chart, line chart, donut chart, data visualization, graph, plot, sparkline, analytics dashboard, PieChart, BarChart, LineChart, PieChartDatum, LineChartSeries, LineChartPoint, series, axis, legend
- **component-vocabulary** (`docs/Ikon.Agent.Docs/guides/component-vocabulary.md`): component enum, component record, event args type, callback argument type, ActionEvent, CaptureImageMode, DragEndArgs, ToastItem, CarouselBreakpoint, CellType, CheckedState, FormMessageMatch, ExpandedSet, ScrollNearEndArgs, LineCurve, LegendConfig, ChartClickArgs, UIViewNode
- **consent** (`docs/Ikon.Agent.Docs/guides/consent.md`): consent, permission, opt-in, opt out, withdraw consent, GDPR, privacy, app.Consent, ConsentService, ConsentPurposes, ConsentPurpose, ConsentRecord, ConsentAnswer, ConsentRequiredException, IsGranted, Require, Grant, Revoke, RecordsFor, usage measurement, analytics consent, AI provider consent
- **costs** (`docs/Ikon.Agent.Docs/guides/costs.md`): cost, costs, credits, spend, budget, usage, billing, app.Costs, CostsService, CostQuery, DailyCost, CostScopeFilter, how much did this cost, per-user cost, attribution
- **crosswind-reference** (`docs/Ikon.Agent.Docs/guides/crosswind-reference.md`): crosswind guide, UI design patterns, common pitfalls, sophisticated UI, layout patterns, gradient, overlay, CRT, scanline
- **csharp-primer** (`docs/Ikon.Agent.Docs/guides/csharp-primer.md`): C# 14, modern C#, dictionary literal, collection expression, primary constructor, raw string literal, async, await, ValueTask, IAsyncEnumerable, target typing, nullable reference types, records, pattern matching, file-scoped namespace, top-level statements, modern idioms, enterprise patterns, abstractions, factory, IUnitOfWork, dependency injection, mock, interface, abstract base class, syntax error, CS1003, CS1525, CS1026, CS0173, CS8917, CS1593, CS0234
- **databases** (`docs/Ikon.Agent.Docs/guides/databases.md`): databases, PostgreSQL, SQL, db, app.Database, AppDatabaseConnection, EF Core, Entity Framework, DbContext, migrations, db migrate, LINQ, seed, app.Seed, staging, test data
- **email** (`docs/Ikon.Agent.Docs/guides/email.md`): email, send email, app.Email, EmailSendRequest, EmailAttachment, sender identity, senderLocalPart, senderDisplayName, senderDomain, verified sending domain, reply-to, attachments, inbox, inbound email, EmailSenderNotAvailableException
- **emergence-strategies** (`docs/Ikon.Agent.Docs/guides/emergence-strategies.md`): mapreduce, treesearch, critic, refine, ensemble, bestof, advanced patterns
- **emergence** (`docs/Ikon.Agent.Docs/guides/emergence.md`): emergence, emerge run, structured output, json, tools, agent, bestof, mapreduce, patterns, cancellation, timeout
- **endpoints-webhooks** (`docs/Ikon.Agent.Docs/guides/endpoints-webhooks.md`): endpoints, webhooks, HTTP, HTTPS, REST, MCP, HttpGet, HttpPost, HttpPut, Mcp, WebSocket, TCP, TLS, UDP, public URL, tunneling, AppEndpointHost, function
- **frontend-fundamentals** (`docs/Ikon.Agent.Docs/guides/frontend-fundamentals.md`): frontend, SDK, auth, connection, i18n, styling, query params, custom UI component, module, resolver, React, magic link login email template, emails folder
- **function-registry** (`docs/Ikon.Agent.Docs/guides/function-registry.md`): function registry, registration, attribute, visibility, LLM tools, callable functions
- **location** (`docs/Ikon.Agent.Docs/guides/location.md`): location, gps, geolocation, app.Locations, LocationService, StartTrackingAsync, StopTrackingAsync, OnUpdate, LocationUpdate, LocationTrackingOptions, background location, continuous location, live tracking, tracker app, courier tracking, delivery tracking, ride tracking, foreground service, background mode, GetLocationAsync
- **logging** (`docs/Ikon.Agent.Docs/guides/logging.md`): log, logging, debug, warning, error, diagnostics
- **media-analysis** (`docs/Ikon.Agent.Docs/guides/media-analysis.md`): ffmpeg, ffprobe, video analysis, audio analysis, media metadata, duration, codec, transcode, extract frames, extract audio, thumbnail, waveform, large file, video upload, audio upload, streaming analysis, media file, video file, audio file
- **motion-reference** (`docs/Ikon.Agent.Docs/guides/motion-reference.md`): motion spec, keyframe, animation, timing, staggered text, 3D transform, filter animation, animatable properties, motion syntax grammar
- **notifications** (`docs/Ikon.Agent.Docs/guides/notifications.md`): notifications, push, push notification, app.Notifications, NotificationContent, SendToUserAsync, SendToSessionAsync, BroadcastAsync, permission, offline push, web push, FCM, alert, toast, NotificationInbox, inbox, in-app notifications, unread, NotificationRoute, NotificationReach, all devices, multidevice, INotificationChannel, email notification, SMS notification, Telegram, WhatsApp, mute
- **parallax-overview** (`docs/Ikon.Agent.Docs/guides/parallax-overview.md`): parallax, server-driven UI, reactive UI, UI.Root, setting up a UI, component catalogue, which component, two-way binding, styling with crosswind, scroll area, auto-scroll, panzoom, boot snapshot, skeleton, SEO, architecture
- **payments** (`docs/Ikon.Agent.Docs/guides/payments.md`): payments, charge end users, monetize, paywall, subscription, recurring, one-off payment, refund, Stripe, Mollie, app.Payments, CreatePaymentLinkAsync, ListOffersAsync, offers, GetEntitlementAsync, entitlement, PaymentsRequireEntitlement, CreateOfferAsync, create offer, PaymentEventReceived, payment events, cancel subscription
- **pipelines-reference** (`docs/Ikon.Agent.Docs/guides/pipelines-reference.md`): pipeline API, transform, processor, pipeline guide
- **pipelines** (`docs/Ikon.Agent.Docs/guides/pipelines.md`): pipeline, background processing, transform, processor, scheduled, cron
- **platform-runtime-types** (`docs/Ikon.Agent.Docs/guides/platform-runtime-types.md`): Toml, TOML config, ServerRunType, local or cloud, SdkType, ProtocolVersion, NameConversions, kebab case, slug, camel case, ExtendedCast, UserException, BackendQuotaExceededException, quota, IMessageChannel, SnapshotCapture, boot snapshot, UIStreamBegin, VideoStreamBegin
- **profiles-and-roles** (`docs/Ikon.Agent.Docs/guides/profiles-and-roles.md`): profile, user profile, roles, admin, moderator, RequireRole, ClientProfiles, ClientProfile, ProfileData, UserRole, RoleRequiredException, custom attributes, IProfileAttributes, MintedUserToken, name, email, address, SsoManagedFields
- **reactive-state** (`docs/Ikon.Agent.Docs/guides/reactive-state.md`): reactive, client reactive, user reactive, persistent reactive, persistent session reactive, persistent user reactive, persistence backend, postgres backend, public asset backend, reactive scope, value mutation
- **secrets** (`docs/Ikon.Agent.Docs/guides/secrets.md`): secrets, tokens, API keys, credentials, passwords, app.Secrets, ikon secret
- **sso** (`docs/Ikon.Agent.Docs/guides/sso.md`): sso, single sign-on, enterprise sign-in, organisation account, Microsoft Entra, Azure AD, Google Workspace, Okta, OpenID Connect, identity provider, IdP, app.SsoConnections, SsoConnectionsService, SsoConnection, SsoPreset, SsoRequiredMode, tenant directory, Context.AuthProvider, Context.SsoConnectionId
- **styling-and-motion** (`docs/Ikon.Agent.Docs/guides/styling-and-motion.md`): crosswind, tailwind, theme constants, style arrays, motion, animation, UI guidelines, theme customization
- **tailwind-reference** (`docs/Ikon.Agent.Docs/guides/tailwind-reference.md`): tailwind spec, utility classes, layout, flexbox, grid, spacing, typography, backgrounds, borders, effects, shadows, transitions
- **telephony** (`docs/Ikon.Agent.Docs/guides/telephony.md`): sms, text message, send sms, phone call, voice call, app.Telephony, SmsSendResult, PlacedCall, TelephonyStatus, phone number, E.164, replyable, hang up, inbound sms
- **theming** (`docs/Ikon.Agent.Docs/guides/theming.md`): theming, IkonTheme, brand palette, color scale, dark mode, design tokens, indexer, theme customization, brand commitment, mood, paint a fresh app, palette by hand, dual theme
- **ui-api-reference** (`docs/Ikon.Agent.Docs/guides/ui-api-reference.md`): component parameters, shared parameters, props, style, styleId, key, ariaLabel, content, value, defaultValue, forceMount, loop, controlled, uncontrolled, where are the signatures, method signatures
- **ui-components** (`docs/Ikon.Agent.Docs/guides/ui-components.md`): layout, overlays, inputs, display, navigation, drag-and-drop, text, button, dialog, tabs, accordion, scroll area, toast, popover, chat interface, message bubbles
- **utilities** (`docs/Ikon.Agent.Docs/guides/utilities.md`): retry, Retrier, backoff, transient failure, embedded resource, Resources, Levenshtein, StringDistance, fuzzy match, ILoggerProvider, IkonLoggerProvider


## API Signatures

Exact signatures for every public platform type live in `docs/Ikon.Agent.Docs/api/`, one file per namespace (`Ikon.Parallax.Components.Standard.md`, `Ikon.App.md`, …), with a `~2`, `~3` suffix where one namespace needs several. These are generated from the compiled assemblies, so they are the authority on a parameter list, a default value or a return type whenever a guide and a signature disagree.

Grep that directory by type or member name before guessing a signature — it is what turns a CS1739 or CS1061 into a one-line fix.


## Code Patterns

`docs/Ikon.Agent.Docs/patterns/` holds short, self-contained patterns lifted from production Ikon AI Apps, each one compiled against the real assemblies and render tested, so a pattern cannot drift from the API it uses.

Start at `docs/Ikon.Agent.Docs/patterns/_index.md`, which groups every pattern by the task it solves. Adapting the closest pattern is faster and safer than writing a screen or a flow from scratch.

<!-- ikon-user-content-below -->
