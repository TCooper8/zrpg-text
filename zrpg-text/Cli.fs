namespace Zrpg

open System

module Cli =
  type ModuleCmd =
    | Cache of key:string * value:string

  type CmdResult =
    | CarryCmd
    | NoOp
    | GetInput of (string -> string * CmdResult)
    | Retry

  type CmdParam =
    | StringParam of paramName:string * info:string

  [<Interface>]
  type ICli =
    abstract member cache: key:string -> value:obj -> unit
    abstract member cacheFind: key:string -> obj option
    abstract member cacheGet: key:string -> 'a 

  type Cmd = {
    name: string
    cmdParams: CmdParam seq
    info: string
    invoke: ICli -> obj array -> string * CmdResult
  }

  type CliModule = {
    name: string
    info: string
    commands: Cmd seq
    //invoke: ICli -> string -> string * CmdResult
  }

  type Cli (cliModules:CliModule seq) =
    let mutable cache = Map.empty<string, obj>

    let moduleTable =
      cliModules
      |> Seq.map (fun cliModule ->
        (cliModule.name, cliModule)
      )
      |> Map.ofSeq

    let autoComplete (input:string) =
      let parts = input.Split(' ') |> Array.toList
      match parts with
      | [] ->
        cliModules
        |> Seq.map (fun cliModule -> cliModule.name)
      | moduleName::[] ->
        match moduleTable.TryFind moduleName with
        | None ->
          cliModules
          |> Seq.filter (fun cliModule -> cliModule.name.Contains(moduleName))
          |> Seq.map (fun cliModule -> cliModule.name)
        | Some cliModule ->
          cliModule.commands
          |> Seq.map (fun cmd -> cmd.name)
      | moduleName::cmdName::[] ->
        match moduleTable.TryFind moduleName with
        | None ->
          Seq.empty
        | Some cliModule ->
          cliModule.commands
          |> Seq.filter (fun cmd -> cmd.name.Contains(cmdName))
          |> Seq.map (fun cmd -> cmd.name)

    interface ICli with
      member this.cache key value =
        cache <- cache |> Map.add key value

      member this.cacheFind key =
        cache |> Map.tryFind key

      member this.cacheGet key =
        let value = cache |> Map.find key
        value :?> 'a

    member private this.invokeCmd (input:string) =
      // First, lookup the module based on the input.
      let parts = input.Split(' ') |> Array.toList
      match parts with
      | moduleName::method::args ->
        match moduleTable.TryFind moduleName with
        | None ->
          sprintf "Module %s does not exist." moduleName, NoOp
        | Some cliModule ->
          cliModule.commands
          |> Seq.tryFind (fun cmd -> cmd.name = method)
          |> Option.map (fun cmd ->
            // Zip the params up with the command params.
            let finalArgs = seq {
              for i in 0 .. (cmd.cmdParams |> Seq.length) - 1 do
              let cmdParam = Seq.item i cmd.cmdParams
              match Seq.tryItem i args with
              | None ->
                invalidArg cmd.info "Was not found in the input"
              | Some arg ->
                match cmdParam with
                | StringParam(name, info) ->
                  yield arg :> obj
                ()
            }
            cmd.invoke (this :> ICli) (finalArgs |> Seq.toArray)

            //"Method not invoked", NoOp
          )
          |> defaultArg <| (sprintf "Method [%s].[%s] does not exist." moduleName method, NoOp)
      | moduleName::[] ->
        // Lookup the module and report the info.
        match moduleTable.TryFind moduleName with
        | None ->
          sprintf "Module %s does not exist." moduleName, NoOp
        | Some cliModule ->
          let lines =
            [ sprintf "Info : %s" cliModule.info
              sprintf "  Module has the following methods : %A"
                <| (cliModule.commands |> Seq.map (fun cmd -> cmd.name))
            ]
          String.Join("\n", lines), NoOp
      | [] ->
        "", CarryCmd

    member this.listen () =
      let rec handleResult (output, result) loop acc =
        match result with
        | NoOp ->
          printfn "\n>>> %s" output
          loop ""

        | CarryCmd ->
          printfn "\n>>> %s" output
          loop acc

        | Retry ->
          handleResult (output, result) loop acc

        | GetInput (continueFn) ->
          printf "\n%s : " output
          let input = Console.ReadLine()
          continueFn input
          |> handleResult
          <| loop
          <| acc

      let rec loop (acc:string) =
        let key = Console.ReadKey(true)
        match key.Key with
        | ConsoleKey.Backspace ->
          Console.CursorLeft <- Math.Max(0, Console.CursorLeft - 1)
          printf " "
          Console.CursorLeft <- Math.Max(0, Console.CursorLeft - 1)
          acc.Substring(0, Math.Max(0, acc.Length - 1)) |> loop

        | ConsoleKey.Enter ->
          try
            this.invokeCmd acc
            |> handleResult
            <| loop
            <| acc
          with e ->
            handleResult
            <| (sprintf "Error: %A" e, NoOp)
            <| loop
            <| acc

        | ConsoleKey.Tab ->
          let suggestions = autoComplete acc
          printfn "\n>>> %A" suggestions
          printf "%s" acc
          loop acc

        | ConsoleKey.Escape ->
          printfn "^C"
          loop ""

        | _ ->
          let char = key.KeyChar.ToString()
          printf "%s" char
          acc + char |> loop

      async {
        while loop "" do ()
      }

  let ofSeq modules =
    Cli modules