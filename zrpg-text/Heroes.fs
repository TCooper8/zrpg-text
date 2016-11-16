namespace Zrpg

open System

module Heroes =
  let schema =
    @"CREATE TABLE [dbo].[Heroes] (
      [Id]        BINARY (16) NOT NULL,
      [Name]      NTEXT       NOT NULL,
      [HeroClass] NTEXT       NOT NULL,
      [Race]      NTEXT       NOT NULL,
      [StatsId]   BINARY (16) NOT NULL,
      [KingdomId] BINARY (16) NOT NULL,
      PRIMARY KEY CLUSTERED ([Id] ASC)
    );
    "

  type HeroClass =
    | Warrior
    with
      override this.ToString () =
        match this with
        | Warrior -> "Warrior"

  let (|HeroClass|_|) (input:string) =
    match input.ToLower() with
    | "warrior" -> Warrior |> Some
    | _ -> None

  type Race =
    | Human
    with
      override this.ToString () =
        match this with
        | Human -> "Human"

  let (|Race|_|) (input:string) =
    match input.ToLower() with
    | "human" -> Human |> Some
    | _ -> None

  type Stats = {
    level: int
    maxHealth: int
    strength: int
  }

  type Hero = {
    id: Guid
    name: string
    heroClass: HeroClass
    race: Race
    stats: Stats
  }

  type GetHero = {
    id: Guid
  }

  type GetHeroRequest = {
    token: string
    cmd: GetHero
  }

  type GetHeroResult =
    | GetHeroOk of GetHeroOk
    | GetHeroNotFound of GetHeroNotFound

  and GetHeroOk = {
    hero: Hero
  }

  and GetHeroNotFound = {
    status: string
  }

  type PostHero = {
    kingdomId: Guid
    name: string
    heroClass: HeroClass
    race: Race
  }

  type PostHeroRequest = {
    token: string
    cmd: PostHero
  }

  type PostHeroResult =
    | PostHeroCreated of PostHeroCreated
    | PostHeroUnauthorized of PostHeroUnauthorized
    | PostHeroConflict of PostHeroConflict

  and PostHeroCreated = {
    id: Guid
  }

  and PostHeroUnauthorized = {
    reason: string
  }

  and PostHeroConflict = {
    reason: string
  }

  [<Interface>]
  type Heroes =
    abstract member post: PostHeroRequest -> PostHeroResult