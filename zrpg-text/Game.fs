﻿namespace Zrpg

open System
open System.Data
open System.Data.Linq
open Microsoft.FSharp.Data.TypeProviders
open Microsoft.FSharp.Linq

open Newtonsoft.Json
open DevOne.Security.Cryptography.BCrypt

open AccountActivations
open Accounts
open Auths
open Kingdoms
open Heroes
open Zones
open Regions

module Game =
  type dbSchema = Microsoft.FSharp.Data.TypeProviders.DbmlFile<"ZrpgDatabase.dbml", ContextTypeName = "ZrpgContext">
  let connectionString = "Data Source=(LocalDB)\MSSQLLocalDB;AttachDbFilename=C:\Users\coope\Documents\zrpg.mdf;Integrated Security=True;Connect Timeout=30"
  let db = new dbSchema.ZrpgContext(connectionString)

  let warnings = false

  type WorldDump = {
    regions: PutRegion seq
    zones: PutZone seq
  }

  //db.Log <- System.Console.Out

  type AccountActivationService () =
    interface AccountActivations with
      member this.post request =
        let id = Guid.NewGuid()
        let row =
          dbSchema.AccountActivations(
            Id = Binary(id.ToByteArray()),
            EmailAddress = request.cmd.emailAddress,
            ScreenName = request.cmd.screenName
          )
        db.AccountActivations.InsertOnSubmit(row)

        try
          db.SubmitChanges()
          PostAccountActivationCreated {
            id = id
          }
        with e ->
          PostAccountActivationFailure {
            reason = e.Message
          }

  type AuthService () =
    interface Auths with
      member this.get request =
        query {
          for row in db.Auths do
          where (row.Login = request.login)
          select row
        }
        |> Seq.tryHead
        |> Option.map (fun row ->
          if BCryptHelper.CheckPassword
            ( request.password,
              row.PasswordHash
            ) then
            // Delete any existing tokens for this login.
            query {
              for row in db.Tokens do
              where (row.AuthId = row.AuthId)
              select row
            }
            |> fun rows ->
              db.Tokens.DeleteAllOnSubmit(rows)

              try
                db.SubmitChanges()
              with e ->
                printfn "Error %A" e

            let tokenId = Guid.NewGuid()
            let tokenRow =
              dbSchema.Tokens(
                Id = Binary(tokenId.ToByteArray()),
                AuthId = row.Id
              )

            let accountRow =
              query {
                for row in db.Accounts do
                where (row.AuthId = tokenRow.AuthId)
                select row
              }
              |> Seq.tryHead
              |> Option.map (fun account ->
                { id = Guid(account.Id.ToArray())
                  authId = Guid(account.AuthId.ToArray())
                  kingdomId = Guid(account.KingdomId.ToArray())
                  emailAddress = account.EmailAddress
                }
              )

            match accountRow with
            | None ->
              GetAuthUnauthorized {
                reason = "Account is not linked to identity."
              }
            | Some account ->
              db.Tokens.InsertOnSubmit(tokenRow)

              try
                db.SubmitChanges()

                GetAuthOk {
                  token = tokenId.ToString()
                  account = account
                }
              with e ->
                GetAuthFailure {
                  reason = e.Message
                }
          else
            GetAuthUnauthorized {
              reason = "Invalid credentials"
            }
        )
        |> defaultArg <| GetAuthUnauthorized {
          reason = "Invalid credentials"
        }

      member this.post request =
        let id = Guid.NewGuid()
        let row =
          dbSchema.Auths(
            Id = Binary(id.ToByteArray()),
            Login = request.login,
            PasswordHash = request.passwordHash
          )
        db.Auths.InsertOnSubmit(row)

        try
          db.SubmitChanges()
          PostAuthCreated {
            id = id
          }
        with e ->
          PostAuthConflict {
            reason = e.Message
          }

  type KingdomService () =
    interface Kingdoms with
      member this.get request =
        let kingdomRow =
          query {
            for kingdom in db.Kingdoms do
            where (kingdom.Id = Binary(request.cmd.id.ToByteArray()))
            select kingdom
            exactlyOne
          }
        let heroes =
          query {
            for heroRow in db.Heroes do
            join statsRow in db.Stats
              on (heroRow.StatsId = statsRow.Id)
            where (heroRow.KingdomId = Binary(request.cmd.id.ToByteArray()))
            select (heroRow, statsRow)
          }
          |> Seq.map (fun (heroRow, statsRow) ->
            let heroClass =
              match heroRow.HeroClass with
              | "Warrior" -> Warrior
              | heroClass ->
                sprintf "%s is not a valid hero class" heroClass
                |> failwith
            let race =
              match heroRow.Race with
              | "Human" -> Human
              | race ->
                sprintf "%s is not a valid race" race
                |> failwith

            let stats = {
              level = statsRow.Level
              maxHealth = statsRow.MaxHealth
              strength = statsRow.Strength
            }

            let hero = {
              id = Guid(heroRow.Id.ToArray())
              name = heroRow.Name
              heroClass = heroClass
              stats = stats
              race = race
            }

            hero
          )

        let kingdom = {
          id = request.cmd.id
          screenName = kingdomRow.ScreenName
          heroes = heroes
        }

        GetKingdomOk {
          kingdom = kingdom
        }

      member this.post request =
        let id = Guid.NewGuid()
        let row =
          dbSchema.Kingdoms(
            Id = Binary(id.ToByteArray()),
            ScreenName = request.screenName
          )
        db.Kingdoms.InsertOnSubmit(row)

        try
          db.SubmitChanges()
          PostKingdomCreated {
            id = id
          }
        with e ->
          PostKingdomConflict {
            reason = e.Message
          }

  type AccountService (
    auths:Auths,
    kingdoms:Kingdoms
    ) =
    interface Accounts with
      member this.post request =
        query {
          for row in db.AccountActivations do
          where (row.Id = Binary(request.activationId.ToByteArray()))
          select row
        }
        |> Seq.tryHead
        |> Option.map (fun activation->
          // Now, get the email address from the activation and create an auth for this account.
          let passwordHash =
            BCryptHelper.HashPassword(
              request.password,
              BCryptHelper.GenerateSalt()
            )

          let authPostResult = auths.post {
            login = activation.EmailAddress
            passwordHash = passwordHash
          }
          match authPostResult with
          | PostAuthCreated authCreated ->
            // Create the kingdom for this account.
            let kingdomResult = kingdoms.post {
              screenName = activation.ScreenName
            }

            match kingdomResult with
            | PostKingdomConflict conflict ->
              PostAccountFailure {
                reason = conflict.reason
              }

            | PostKingdomCreated kingdomCreated ->
              let accountId = Guid.NewGuid()
              let row =
                dbSchema.Accounts(
                  Id = Binary(accountId.ToByteArray()),
                  AuthId = Binary(authCreated.id.ToByteArray()),
                  KingdomId = Binary(kingdomCreated.id.ToByteArray()),
                  EmailAddress = activation.EmailAddress
                )
              db.Accounts.InsertOnSubmit(row)

              try
                db.SubmitChanges()
                PostAccountCreated {
                  id = accountId
                }
              with e ->
                PostAccountFailure {
                  reason = e.Message
                }

          | PostAuthConflict conflict ->
            PostAccountFailure {
              reason = conflict.reason
            }
        )
        |> defaultArg <| PostAccountFailure {
          reason = "Invalid activation id"
        }

  type HeroService () =
    interface Heroes with
      member this.post request =
        let id = Guid.NewGuid()
        let statsId = Guid.NewGuid()

        let statsRow =
          dbSchema.Stats(
            Id = Binary(statsId.ToByteArray()),
            Level = 1,
            MaxHealth = 10,
            Strength = 10
          )
        db.Stats.InsertOnSubmit(statsRow)

        let row =
          dbSchema.Heroes(
            Id = Binary(id.ToByteArray()),
            KingdomId = Binary(request.cmd.kingdomId.ToByteArray()),
            Name = request.cmd.name,
            HeroClass = request.cmd.heroClass.ToString(),
            Race = request.cmd.race.ToString(),
            StatsId = Binary(statsId.ToByteArray())
          )
        db.Heroes.InsertOnSubmit(row)

        try
          db.SubmitChanges()
          PostHeroCreated {
            id = id
          }
        with e ->
          PostHeroConflict {
            reason = e.Message
          }

  type ZoneService () =
    interface Zones with
      member this.post request =
        let id = Guid.NewGuid()
        let row =
          dbSchema.Zones(
            Id = Binary(id.ToByteArray()),
            RegionId = Binary(request.cmd.regionId.ToByteArray()),
            Name = request.cmd.name
          )
        db.Zones.InsertOnSubmit(row)

        try
          db.SubmitChanges()
          PostZoneCreated {
            id = id
          }
        with e ->
          PostZoneConflict {
            reason = e.Message
          }

      member this.put request =
        let row =
          dbSchema.Zones(
            Id = Binary(request.cmd.id.ToByteArray()),
            RegionId = Binary(request.cmd.regionId.ToByteArray()),
            Name = request.cmd.name
          )
        db.Zones.InsertOnSubmit(row)

        try
          db.SubmitChanges()
          PutZoneOk {
            status = "created"
          }
        with e ->
          PutZoneConflict {
            reason = e.Message
          }

  type RegionService () =
    interface Regions with
      member this.post request =
        let id = Guid.NewGuid()
        let row =
          dbSchema.Regions(
            Id = Binary(id.ToByteArray()),
            Name = request.cmd.name
          )
        db.Regions.InsertOnSubmit(row)

        try
          db.SubmitChanges()
          PostRegionCreated {
            id = id
          }
        with e ->
          PostRegionConflict {
            reason = e.Message
          }

      member this.put request =
        let row =
          dbSchema.Regions(
            Id = Binary(request.cmd.id.ToByteArray()),
            Name = request.cmd.name
          )
        db.Regions.InsertOnSubmit(row)

        try
          db.SubmitChanges()
          PutRegionOk {
            status = "created"
          }
        with e ->
          PutRegionConflict {
            reason = e.Message
          }

  type GameCmd =
    | PostAccountActivation of PostAccountActivationRequest
    | PostAccount of PostAccount
    | GetAuth of GetAuth

  type GameReply =
    | PostAccountActivationResult of PostAccountActivationResult
    | PostAccountResult of PostAccountResult
    | GetAuthResult of GetAuthResult
       
  type Game () =
    let accountActivations = AccountActivationService() :> AccountActivations
    let auths = AuthService() :> Auths
    let kingdoms = KingdomService() :> Kingdoms
    let accounts =
      AccountService(
        auths,
        kingdoms
      ) :> Accounts
    let heroes = HeroService() :> Heroes
    let regions = RegionService() :> Regions
    let zones = ZoneService() :> Zones

    member this.postHero =
      heroes.post

    member this.getKingdom =
      kingdoms.get

    member this.loadWorld () =
      // TODO: Address world loading requirements.
      // Currently, the world will be loaded in from a json file.
      let worldFilepath = "../../world.json"
      use worldFile = System.IO.File.OpenText(worldFilepath)
      let data = worldFile.ReadToEnd()
      let world = JsonConvert.DeserializeObject<WorldDump>(data)

      // Now, take the world and load in the data.
      for putRegion in world.regions do
        let request: PutRegionRequest = {
          token = ""
          cmd = putRegion
        }
        match regions.put request with
        | PutRegionOk ok -> ()
        | PutRegionConflict conflict ->
          printf "Warning : %A on regions.put %A" conflict putRegion

      for putZone in world.zones do
        let request: PutZoneRequest = {
          token = ""
          cmd = putZone
        }
        match zones.put request with
        | PutZoneOk ok -> ()
        | PutZoneConflict conflict ->
          printf "Warning : %A on regions.put %A" conflict putZone

      () 

    member this.handle cmd =
      match cmd with
      | GetAuth getAuth ->
        auths.get getAuth
        |> GetAuthResult
      | PostAccountActivation posting ->
        accountActivations.post posting
        |> PostAccountActivationResult
      | PostAccount posting ->
        accounts.post posting
        |> PostAccountResult
