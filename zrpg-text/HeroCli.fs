namespace Zrpg

open System

open Zrpg.Utils
open Zrpg.Cli
open Zrpg.Heroes
open Zrpg.Kingdoms
open Zrpg.Game

module HeroCli =
  let heroClassInfo = function
  | Warrior ->
    [ "A warrior is a melee fighter, highly trained in the art of weaponry."
      "  Warriors can choose between two types of specialties, protection and arms."
      "  A Protection warrior specializes in defensive abilities, used to absorb damage rather than dealing it."
      "  An Arms warrior specializes in dealing heavy amounts of damage to overcome their opponents."
    ]
    |> join "\n"

  let raceInfo = function
  | Human ->
    "Humans are native to this world."

  let raceSelection =
    [ "Human"
    ]

  let heroClassSelection = 
    [ "Warrior"
    ]

  let createCmd: Game -> Cmd = fun game ->
    let validHeroName (heroName:string) =
      true

    let chooseHeroName (cli:ICli) (race:Race) (heroClass:HeroClass) =
      let output =
        [ "What would you like to name your hero?"
        ]
        |> join "\n"

      output, (GetInput <| fun heroName ->
        if validHeroName heroName then
          printf "Creating your hero..."

          let token: string = cli.cacheGet "token"
          let kingdom: Kingdom = cli.cacheGet "kingdom"

          let result = game.postHero {
            token = token
            cmd =
              { kingdomId = kingdom.id
                name = heroName
                heroClass = heroClass
                race = race
              }
          }

          "Hero created!", NoOp
        else
          "Invalid hero name, try again.", Retry
      )

    let chooseHeroClass (cli:ICli) (race:Race) =
      let output =
        [ "Please select a hero class from the following options :"
          heroClassSelection |> Seq.map (fun heroClass ->
            sprintf "  %s" heroClass
          ) |> join "\n"
        ]
        |> join "\n"

      output, (GetInput <| fun classInput ->
        let heroClass =
          match classInput with
          | "Warrior" | "warrior" -> Warrior
          | _ ->
            sprintf "Hero class %s is not a valid class" classInput
            |> invalidArg "class"

        chooseHeroName cli race heroClass
      )

    let chooseRace (cli:ICli) =
      let output =
        [ "Please select a race from the following options :"
          raceSelection |> Seq.map (fun race ->
            sprintf "  %s" race
          ) |> join "\n"
        ]
        |> join "\n"

      output, (GetInput <| fun raceInput ->
        let race =
          match raceInput with
          | "Human" | "human" -> Human
          | _ ->
            sprintf "Race %s is not a valid race" raceInput
            |> invalidArg "race"

        chooseHeroClass cli race
      )

    { name = "create"
      info = "Create a new hero!"
      cmdParams = [ ]
      invoke = fun cli args ->
        match cli.cacheFind "token" with
        | None -> invalidArg "token" "You need to login using 'account login' before creating a hero."
        | Some value ->
          match value with
          | :? string as value -> ()
          | _ -> invalidArg "token" "Error : token is not a valid type. Try logging in again."

        match cli.cacheFind "kingdom" with
        | None -> invalidArg "kingdom" "Error : kingdom is not cached. Try logging in again."
        | Some value ->
          match value with
          | :? Kingdom as value -> ()
          | _ -> invalidArg "kingdom" "Error : kingdom is in an unexpected state. Try logging in again."

        chooseRace cli
    }

  let listCmd: Game -> Cmd = fun game ->
    { name = "list"
      info = "Show a list of your heroes."
      cmdParams = []
      invoke = fun cli args ->
        let kingdom =
          match cli.cacheFind "kingdom" with
          | None -> invalidArg "kingdom" "Error : kingdom is not cached. Try logging in again."
          | Some value ->
            match value with
            | :? Kingdom as value -> value
            | _ -> invalidArg "kingdom" "Error : kingdom is in an unexpected state. Try logging in again."
        let heroes = kingdom.heroes 

        let report =
          heroes
          |> Seq.mapi (fun i hero ->
            sprintf "[%i] | %s, level %i %A %A"
            <| i
            <| hero.name
            <| hero.stats.level
            <| hero.race
            <| hero.heroClass
          )
          |> join "\n"

        let report =
          sprintf "Heroes : \n%A" report

        report, Cli.NoOp
    }

  let viewHero (hero:Hero) =
    let heading = 
      sprintf "Info : %s, level %i %A %A"
      <| hero.name
      <| hero.stats.level
      <| hero.race
      <| hero.heroClass

    let stats =
      [ "Stats :"
        sprintf "  Strength  = %i" hero.stats.strength
        sprintf "  MaxHealth = %d" hero.stats.maxHealth
      ]
      |> join "\n"

    [ heading
      stats
    ]
    |> join "\n"

  let infoCmd: Game -> Cmd = fun game ->
    { name = "info"
      info = "View information about the hero."
      cmdParams = []
      invoke = fun cli args ->
        let output, _ = (listCmd game).invoke cli args
        printf "%s" output

        let kingdom: Kingdom = cli.cacheGet "kingdom"

        "Now, which hero would you like to view?" , (GetInput <| fun heroSelection ->
          let hero =
            kingdom.heroes
            |> Seq.tryFind (fun hero ->
                 (hero.name = heroSelection)
              || (hero.name.ToLower() = heroSelection.ToLower())
            )
          match hero with
          | None ->
            // ?? Maybe the user has duplicate names for a hero, try the index.
            match Int32.TryParse heroSelection with
            | false, _ ->
              "Could not match your selection to a hero. Try again.", Retry
            | true, index ->
              let hero =
                kingdom.heroes
                |> Seq.tryItem index

              match hero with
              | None ->
                "Could not match your selection to a hero. Try again.", Retry
              | Some hero ->
                viewHero hero, NoOp
          | Some hero ->
            viewHero hero, NoOp
        )
    }

  let cli: Game -> CliModule = fun game ->
    { name = "hero"
      info = "Manager your heroes from here."
      commands =
        [ createCmd game
          listCmd game
          infoCmd game
        ]
    }