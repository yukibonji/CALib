﻿module KDGame
open CA
open FSharp.Collections.ParallelSeq

type Action = 
    | Hawk
    | Dove

type GameType = {Name : string}
type Opponents = Id[]
type PayoffMatrix = (Action * Action) -> (float*float)
type Strategy = (Action*float)[] //probability of playing action
type TypeStrategy = GameType * Strategy

type Game =
    {
        Player1  : GameType
        Player2  : GameType
        Payoff   : PayoffMatrix
    }  

let inline yourself x = x

let solve (g:Game)  =  [g.Player1,[| Hawk, 0.2 ; Dove, 0.8 |]; g.Player1,[| Hawk, 0.8 ; Dove, 0.2 |]]

let opponents pop network =
    let opponents = pop |> PSeq.collect (fun p -> network pop p.Id |> Seq.map (fun p2 -> set[p.Id; p2.Id])) |> set
    opponents |> Seq.map Set.toArray |> Seq.toArray

let individualStrategies game pop  : TypeStrategy[] = 
    let strategies = solve game
    pop |> Array.map (fun i -> strategies.[CAUtils.rnd.Value.Next(2)])

let playAction strategy = 
    //probability dist so it accumluates to 1.
    let rnd = CAUtils.rnd.Value.NextDouble()
    let rec sel (strategy:(Action*float)[]) rnd cumP i =
        let (a,p) = strategy.[i]
        let cumP = cumP + p
        if rnd >= cumP then
            sel strategy rnd cumP (i+1)
        else
            a
    sel strategy rnd 0. 0
        
let playGame game (strategies:TypeStrategy[]) i (opponents:Opponents) =
    let p1 = opponents.[0]
    let p2 = opponents.[1]
    let s1 = strategies.[p1]
    let s2 = strategies.[p2]
    let a1 = playAction (snd s1)
    let a2 = playAction (snd s2)
    let pay1,pay2 = game.Payoff (a1,a2)
    [(p1,pay1);(p2,pay2)]

let playAllGames opponents game strategies = 
    opponents 
    |> PSeq.mapi (playGame game strategies) 
    |> PSeq.collect yourself 
    |> PSeq.groupBy fst
    |> PSeq.map (fun (i,xs) -> i, xs |> PSeq.sumBy snd)
    |> PSeq.sortBy fst
    |> PSeq.map snd
    |> PSeq.toArray

let scaler (sMin,sMax) (vMin,vMax) (v:float) =
    if v < vMin then failwith "out of min range for scaling"
    if v > vMax then failwith "out of max range for scaling"
    (v - vMin) / (vMax - vMin) * (sMax - sMin) + sMin
    (*
    scaler (0.1, 0.9) (10., 500.) 223.
    scaler (0.1, 0.9) (10., 500.) 10.
    scaler (0.1, 0.9) (10., 500.) 500.
    scaler (0.1, 0.9) (-200., -100.) -110.
    *)

let fitness (i:Individual) = i.Fitness

let rec fixedStrategyKD sign opponents game strategies (pop:Individual[],beliefSpace) (network:Network) =
    let targetRange = (0.1,0.9)
    //payoff
    let payoffs = playAllGames opponents game strategies
    let minP = Array.min payoffs
    let maxP = Array.max payoffs
    let scaledPayoffs = payoffs |> Array.map (scaler targetRange (minP,maxP))
    //fitness
    let minFit = pop|>Array.minBy (fun p->p.Fitness) |> fitness
    let maxFit = pop|>Array.maxBy (fun p->p.Fitness) |> fitness
    let sourceRange = if sign = 1. then (minFit,maxFit) else (-maxFit,-minFit)
    let sf f = scaler targetRange sourceRange (sign * f)
    let scaledFitness = pop |> Array.mapi (fun i p -> scaledPayoffs.[i] * sf p.Fitness)
    let pop =
        pop
        |> PSeq.map (fun p ->
            let friends = network pop p.Id
            let maxP = Seq.append friends [p] |> Seq.maxBy (fun p->scaledFitness.[p.Id])
            {p with KS=maxP.KS}
        )
        |> PSeq.toArray
    pop,beliefSpace,KD(fixedStrategyKD sign opponents game strategies)

let hawkDovePayoff = function
    | Hawk,Hawk -> 0.2, 0.2
    | Dove,Dove -> 0.5, 0.5
    | Hawk,Dove -> 0.8, 0.1
    | Dove,Hawk -> 0.1, 0.8

let hawkDoveGame = {Player1={Name="Hawk"}; Player2={Name="Dove"};Payoff=hawkDovePayoff}

let gtKnowledgeDist minmax game pop network =
    let sign = if minmax 2. 1. then +1. else -1.
    let opponents = opponents pop network 
    let strategies = individualStrategies game pop
    KD(fixedStrategyKD sign opponents game strategies)
    
