
open Zrpg
open Zrpg.Auths
open Zrpg.AccountActivations
open Zrpg.Accounts
open Zrpg.Heroes
open Zrpg.Game
open Zrpg.Kingdoms

open System

[<EntryPoint>]
let main argv = 
  let game = Game()

  let login email password =
    game.handle
    <| GetAuth {
      login = email
      password = password
    }
    |> fun reply ->
      match reply with
      | GetAuthResult (GetAuthOk authOk) ->
        printfn "AuthOk of %A" authOk
        Some authOk
      | GetAuthResult (GetAuthUnauthorized authUnauthorized) ->
        printfn "Login failed with %A" authUnauthorized
        None
      | GetAuthResult (GetAuthFailure authFailure) ->
        printfn "Login failed with %A" authFailure
        None

  let createAccount () =
    game.handle
    <| PostAccountActivation
      { token = "bob"
        cmd = {
          emailAddress = "bobby@gmail.com"
          screenName = "bobby"
        }
      }
    |> fun reply ->
      match reply with
      | PostAccountActivationResult result ->
        match result with
        | PostAccountActivationCreated activationCreated ->
          printfn "Created activation %A" activationCreated

          game.handle
          <| PostAccount {
            activationId = activationCreated.id
            password = "1234"
          }
          |> fun reply ->
            match reply with
            | PostAccountResult accountResult ->
              match accountResult with
              | PostAccountCreated accountCreated ->
                printfn "Account created with %A" accountCreated

              | PostAccountFailure accountFailure ->
                printfn "Account failure with %A" accountFailure
                // Try to login.

        | PostAccountActivationFailure activationFailure ->
          printfn "Failure to create activation %A" activationFailure
    |> ignore

  let cli =
    let rec accountCli: Cli.CliModule = {
      name = "account"
      info = "Used to create and manipulate accounts."
      commands =
        [ { name = "create"
            cmdParams =
              [ Cli.StringParam("emailAddress", "The email address of the account.")
                Cli.StringParam("screenName", "The screen name of the user under this account.")
              ]
            info = "Create a new use account."
            invoke = fun cli args ->
              let emailAddress = args.[0] :?> string
              let screenName = args.[1] :?> string

              let result = game.handle <| PostAccountActivation {
                token = ""
                cmd = {
                  emailAddress = emailAddress
                  screenName = screenName
                }
              }
              match result with
              | PostAccountActivationResult (PostAccountActivationCreated activation) ->
                "Account awaiting activation, please input password", (Cli.GetInput <| fun password ->
                  let result = game.handle <| PostAccount {
                    activationId = activation.id
                    password = password
                  }
                  match result with
                  | PostAccountResult (PostAccountFailure failure) ->
                    sprintf "Unable to create account : %A" failure, Cli.NoOp
                  | PostAccountResult (PostAccountCreated account) ->
                    printf "Account created. . . Logging in. . ."

                    match login emailAddress password with
                    | None ->
                      "Failed to login.", Cli.NoOp
                    | Some auth ->
                      cli.cache "token" auth.token
                      cli.cache "accountId" <| auth.account.id.ToString()
                      cli.cache "authId" <| auth.account.authId.ToString()
                      cli.cache "kingdomId" <| auth.account.kingdomId.ToString()
                      cli.cache "emailAddress" <| auth.account.emailAddress

                      "Logged in!", Cli.NoOp
                )
          }
          { name = "login"
            cmdParams =
              [ Cli.StringParam("emailAddress", "The email address of the login account.")
              ]
            info = "To login to an account."
            invoke = fun cli args ->
              let emailAddress = args.[0] :?> string
              "Please input your password to login", (Cli.GetInput <| fun password ->
                match login emailAddress password with
                | None ->
                  "Invalid credentials.", Cli.NoOp
                | Some auth ->
                  cli.cache "token" auth.token
                  cli.cache "accountId" <| auth.account.id
                  cli.cache "authId" <| auth.account.authId
                  cli.cache "kingdomId" <| auth.account.kingdomId
                  cli.cache "emailAddress" <| auth.account.emailAddress

                  let kingdom =
                    let result = game.getKingdom {
                      token = auth.token
                      cmd = {
                        id = auth.account.kingdomId
                      }
                    }
                    match result with
                    | GetKingdomOk ok ->
                      ok.kingdom
                    | GetKingdomNotFound status ->
                      sprintf "Unable to retrieve kingdom: %A" status
                      |> failwith

                  cli.cache "kingdom" kingdom

                  "Logged in!", Cli.NoOp
              )
          }
          { name = "help"
            cmdParams = []
            info = "The help menu."
            invoke = fun cli args ->
              let cmdLines =
                accountCli.commands
                |> Seq.map (fun cmd ->
                  let argLines =
                    [ for param in cmd.cmdParams do
                      match param with
                      | Cli.StringParam(name, info) ->
                        yield sprintf "    %s [string] - %s" name info
                    ]
                    |> fun ls -> String.Join("\n", ls)
                  let signature =
                    [ for param in cmd.cmdParams do
                      match param with
                      | Cli.StringParam(_, _) ->
                        yield "string"
                    ]
                    |> fun ls -> String.Join(" -> ", ls)

                  sprintf "  account %s: %s\n%s" cmd.name signature argLines
                )
                |> fun ls -> String.Join("\n\n", ls)
              let output =
                sprintf "Module [%s] This module has the following commands : \n%s"
                <| accountCli.name
                <| cmdLines
              output, Cli.NoOp
          }
        ]
    }

    Zrpg.Cli.ofSeq
    <| [
      accountCli
      HeroCli.cli game
    ]

  cli.listen () |> Async.RunSynchronously

  0
