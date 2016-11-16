namespace Zrpg

open System
open System.Data
open System.Data.Linq
open Microsoft.FSharp.Data.TypeProviders
open Microsoft.FSharp.Linq

open DevOne.Security.Cryptography.BCrypt

open AccountActivations
open Accounts
open Auths
open Kingdoms
open Heroes

module Game =
  type dbSchema = Microsoft.FSharp.Data.TypeProviders.DbmlFile<"ZrpgDatabase.dbml", ContextTypeName = "ZrpgContext">
  let connectionString = "Data Source=(LocalDB)\MSSQLLocalDB;AttachDbFilename=C:\Users\coope\Documents\zrpg.mdf;Integrated Security=True;Connect Timeout=30"
  let db = new dbSchema.ZrpgContext(connectionString)
  //type dbSchema = SqlDataConnection<"Data Source=(LocalDB)\MSSQLLocalDB;AttachDbFilename=C:\Users\coope\Documents\zrpg.mdf;Integrated Security=True;Connect Timeout=30">
  //let db = dbSchema.GetDataContext()

  let warnings = false

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
          if BCryptHelper.CheckPassword(
            request.password,
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

        let heroClassCol =
          match request.cmd.heroClass with
          | Warrior -> "Warrior"
        let raceCol =
          match request.cmd.race with
          | Human -> "Human" 

        let row =
          dbSchema.Heroes(
            Id = Binary(id.ToByteArray()),
            KingdomId = Binary(request.cmd.kingdomId.ToByteArray()),
            Name = request.cmd.name,
            HeroClass = heroClassCol,
            Race = raceCol,
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

    member this.postHero =
      heroes.post

    member this.getKingdom =
      kingdoms.get

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
