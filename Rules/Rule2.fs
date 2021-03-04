module BVTProver.RewriteRules.Rule2
open BVTProver
open Formula
open MathHelpers
open FormulaActions


type BoundingInequality =
    | Upper of int*Term
    | Lower of int*Term
    static member tuplify = function Upper (a, b) | Lower (a, b) -> (a, b)
    static member is_upper = function Upper _ -> true | _ -> false
       
type RuleType = All | Any
let (|Bounds|_|) x (conjunct: Formula) = 
    
    match conjunct with
        | AsLe (AsMult (ThisVar x, Int d | Int d, ThisVar x), FreeOf x t) -> Some (Upper(d, t)) // β×x ≤ b
        | AsLt (FreeOf x t, AsMult (ThisVar x, Int d | Int d, ThisVar x)) -> Some (Lower(d, t)) // a < α×x
        | _ -> None

let (|Rule2|_|) (M: Map<string, int>) x (cube: Cube) =
    let (|Bounds|_|) = (|Bounds|_|) x
    
    // todo: lazy computation
    if cube.each_matches (|Bounds|_|) then
        let bounds = List.choose (|Bounds|_|) cube.conjuncts
        let tuples = List.map BoundingInequality.tuplify bounds
        let LCM = tuples |> (List.map fst) |> lcmlist
        let side_condition num t = t <== Int((Term.MaxNumber)/(LCM/num))
    
        let var_value = M.Item(match x with | Var s -> s)
        // side conditions
        let lcm_overflows = LCM >= Term.MaxNumber
        
        let lcm_multiplied_overflows = var_value * LCM > Term.MaxNumber
        let model_satisfies = List.forall (fun (n, t) -> M |= (side_condition n t) ) tuples 

        if not lcm_overflows
           && not lcm_multiplied_overflows
           && model_satisfies then
            Some(LCM, bounds)
        else
            None
    else
        None

let apply_rule2 M x (cube: Cube) (lcm, bounds) =
    let upper_bounds, lower_bounds = List.partition BoundingInequality.is_upper bounds
    let interpreted = function | Upper (num, t) | Lower (num, t) -> (interpret_term M t) * (lcm / num)
 
    
    let sup = upper_bounds |> List.minBy interpreted |> BoundingInequality.tuplify
    let inf = lower_bounds |> List.maxBy interpreted |> BoundingInequality.tuplify
    

    let coefficient_L = fst inf
    let coefficient_U = fst sup
    let term_L = snd inf
    let term_U = snd sup
    
    let side_constraint c t = t <== (Int(Term.MaxNumber / (lcm / c)))
    let mk_constraints_on_bounds = function | Lower (num, t) | Upper (num, t) -> side_constraint num t
    
    let make_conjunct2 conjunct =
        match conjunct with
            | Upper (num, t) when num <> coefficient_U && t <> term_U ->
                        Some((t* (Int(lcm / num)) <== term_L * (Int(lcm / coefficient_L))))
            | Lower (num, t) when num <> coefficient_L && t <> term_L ->
                        Some((term_U * (Int(lcm / coefficient_U)) <== t * (Int(lcm / num))))
            | _ -> None

    let c1 = lower_bounds |> List.map mk_constraints_on_bounds
    let c2 = upper_bounds |> List.map mk_constraints_on_bounds

    let c3 = cube.conjuncts
                |> (List.choose ((|Bounds|_|) x))
                |> (List.choose make_conjunct2)

    let c4 = Div(term_L * (Int(lcm / coefficient_L)), lcm) <! Div(term_L * (Int(lcm / coefficient_L)), lcm)
    c4 :: (c1 @ c2 @ c3)  |> Cube