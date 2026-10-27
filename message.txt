use crate::{bytecode::*, vm::Value::List};
use core::panic;
use std::{collections::HashMap, fmt::{self}, vec};

#[derive(Debug, Clone, PartialEq)]
pub enum Value {
    String(String),
    Integer(i64),
    Float(f64),
    List {
        element_type: Option<String>, // Or an `Option<TypeKind>` enum
        items: Vec<Value>,
    },
    Dictionary {
        key_type: Option<String>,
        value_type: Option<String>,
        allow_duplicates: Option<bool>,
        keys: Vec<Value>,
        values: Vec<Value>,
    },
    Boolean(bool),
    Possible,
    Nil,
}

impl fmt::Display for Value {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            // Depending on it's typing
            Value::String(s) => write!(f, "{}", s),
            Value::Integer(n) => write!(f, "{}", n),
            Value::Float(fl) => write!(f, "{}", fl),
            Value::List { items, element_type: _} => {
                // We also want to format the inside items too!
                let formatted_items: Vec<String> = items.iter().map(|item| item.to_string()).collect();
                write!(f, "[{}]", formatted_items.join(", "))
            }
            Value::Dictionary {key_type: _, value_type: _, allow_duplicates: _, keys, values} => {
                // Just an empty one
                if keys.is_empty() {
                    return write!(f, "{{}}");
                }

                // Map each key-value pair into a formatted string with indentation (nice formatting, ey?)
                let entries: Vec<String> = keys
                    .iter()
                    .zip(values.iter())
                    .map(|(k, v)| format!("    ({}, {})", k, v))
                    .collect(); // This is the final Vector

                // Enclose in curly brackets with newline-separated indented entries
                write!(f, "{{\n{}\n}}", entries.join(",\n"))
            }
            Value::Boolean(b) => write!(f, "{}", b),
            Value::Possible => write!(f, "possible"),
            Value::Nil => write!(f, "nil"),
        }
    }
}

// What's very nice about implementing unto Value is that now ANY Value object can use this!
impl Value {

    /// Compares if the Value types of this and another value are the same. This ignores values within the Value Type
    pub fn compare_valuetypes(&self, other: &Value) -> bool {
        // Simply check if they are of the same value type (NOT THE VALUES INSIDE!)
        std::mem::discriminant(self) == std::mem::discriminant(other)
    }

    /// Evaluates equality ("==") matching the three valued logic of Boolean-Likes as well
    pub fn evaluate_equal(&self, other: &Value) -> Value {
        match (self, other) {
            // False takes highest priority when comparing booleans
            (Value::Boolean(false), _) | (_, Value::Boolean(false)) => Value::Boolean(false),

            // A very possible comparison!
            (Value::Possible, _) | (_, Value::Possible) => Value::Possible,

            // A True comparison
            (Value::Boolean(true), Value::Boolean(true)) => Value::Boolean(true),

            // Just the normie types
            (Value::Integer(a), Value::Integer(b)) => Value::Boolean(a == b),
            (Value::Float(a), Value::Float(b)) => Value::Boolean(a == b),
            (Value::Integer(a), Value::Float(b)) => Value::Boolean((*a as f64) == *b),
            (Value::Float(a), Value::Integer(b)) => Value::Boolean(*a == (*b as f64)),

            (Value::String(a), Value::String(b)) => Value::Boolean(a == b),
            (Value::Nil, Value::Nil) => Value::Boolean(true),

            _ => Value::Boolean(false),
        }
    }

    /// Evaluates inequality ("!="), with normal and possible logic
    pub fn evaluate_not_equal(&self, other: &Value) -> Value {
        match (self, other) {
            // Possible takes highest priority for inequality
            (Value::Possible, _) | (_, Value::Possible) => Value::Possible,

            // Standard boolean inequality check
            (Value::Boolean(a), Value::Boolean(b)) => Value::Boolean(a != b),

            // Other types fall back to negating equality
            _ => match self.evaluate_equal(other) {
                Value::Boolean(b) => Value::Boolean(!b),
                Value::Possible => Value::Possible,
                _ => Value::Boolean(true),
            },
        }
    }

    /// Relational operators (>, >=, <, <=) with Possible propagation
    pub fn evaluate_relational(&self, op: &str, other: &Value) -> Value {
        // If either side is Possible, just return possible. Some say that's impossible 😂
        if matches!(self, Value::Possible) || matches!(other, Value::Possible) {
            return Value::Possible;
        }

        let is_true = match (self, other) {
            // Integer and float comparisons are handled together as well!
            (Value::Integer(a), Value::Integer(b)) => match op {
                ">" => a > b,
                ">=" => a >= b,
                "<" => a < b,
                "<=" => a <= b,
                _ => false,
            },
            (Value::Float(a), Value::Float(b)) => match op {
                ">" => a > b,
                ">=" => a >= b,
                "<" => a < b,
                "<=" => a <= b,
                _ => false,
            },
            (Value::Integer(a), Value::Float(b)) => match op {
                ">" => (*a as f64) > *b,
                ">=" => (*a as f64) >= *b,
                "<" => (*a as f64) < *b,
                "<=" => (*a as f64) <= *b,
                _ => false,
            },
            (Value::Float(a), Value::Integer(b)) => match op {
                ">" => *a > (*b as f64),
                ">=" => *a >= (*b as f64),
                "<" => *a < (*b as f64),
                "<=" => *a <= (*b as f64),
                _ => false,
            },
            _ => false,
        };

        Value::Boolean(is_true)
    }
}

pub struct VirtualMachine {
    stack: Vec<Value>,
    scopes: Vec<HashMap<String, Value>>,
    temporaries: HashMap<u64, Value>
}

impl VirtualMachine {
    pub fn new() -> Self {
        Self {
            stack: Vec::new(),
            scopes: vec![HashMap::new()],
            temporaries: HashMap::new()
        }
    }

    /// The heart of the VM and what allows it to execute all bytecode instructions
    /// Must have a Bytecode param passed in
    pub fn run(&mut self, bytecode: Bytecode) {
        let mut ip = 0; // The instruction pointer

        // We use a while loop in order to allow ip to be mutable and changed within the loop!
        while ip < bytecode.instructions.len() {
            let instr = &bytecode.instructions[ip];

            match instr.op_code.as_str() {
                "Constant" => {
                    // Get value
                    if let Some(val) = &instr.operand {
                        // Get as some different types of values
                        if let Some(s) = val.as_str() {
                            // What if it is not just ANY string? hmmmmm?
                            match s {
                                "possible" => self.stack.push(Value::Possible),
                                "nil" => self.stack.push(Value::Nil),
                                _ => self.stack.push(Value::String(s.to_string())),
                            }
                        }
                        else if let Some(n) = val.as_i64() {
                            self.stack.push(Value::Integer(n));
                        }
                        else if let Some(n) = val.as_f64() {
                            self.stack.push(Value::Float(n));
                        } else if let Some(b) = val.as_bool() {
                            self.stack.push(Value::Boolean(b));
                        }
                    }
                }

                "MakeList" => {
                    if let Some(count) = &instr.operand && let Some(val) = count.as_u64() {
                        let mut listables: Vec<Value> = vec![];

                        for _index in 0..val {
                            if let Some(object) = self.stack.pop() {
                                listables.push(object);
                            }
                        }

                        // Huh??? Oh right because we are popping from the stack
                        // The elements put last are first in this list
                        // So we reverse it to correct the order
                        listables.reverse();

                        let listof: Value = Value::List { items: listables, element_type: None };

                        self.stack.push(listof);
                    }
                }

                "MakeDictionary" => {
                    if let Some(operand) = &instr.operand {
                        let entry_count = operand.as_u64().unwrap_or(0);

                        let mut keys_list: Vec<Value> = vec![];
                        let mut values_list: Vec<Value> = vec![];

                        for _count in 0..entry_count {
                            // We pop value first because we are starting from the top of the stack. Remember?
                            let val = self.stack.pop().unwrap_or(Value::Nil);
                            let key = self.stack.pop().unwrap_or(Value::Nil);

                            // We insert it at the beginning, because AGAIN!
                            // We are taking values from popping the stack, which is in reverse order!
                            keys_list.insert(0, key);
                            values_list.insert(0, val);
                        }

                        let dictionary: Value = Value::Dictionary { key_type: None, value_type: None, allow_duplicates: None, keys: keys_list, values: values_list };

                        self.stack.push(dictionary);
                    }
                }

                // Legit... It just pops from the stack
                "Pop" => { self.stack.pop(); return; }

                /*
                 -----------------
                 --- VARIABLES ---
                 -----------------
                */
                
                // Push a new local variable scope onto the stack
                "EnterScope" => {
                    self.scopes.push(HashMap::new());
                }

                // Pop the innermost scope, destroying its local variables along with it
                "ExitScope" => {
                    if self.scopes.len() > 1 {
                        self.scopes.pop();
                    } else {
                        // Error for the global scope
                        eprintln!("[IP {:04}] Attempted to exit global scope!", ip);
                    }
                }

                // Store a variable within the most local scope concurrently
                "Store" => {
                    if let Some(operand_json) = &instr.operand {
                        let var_info: StoreOperand = serde_json::from_value(operand_json.clone())
                            .expect("Invalid Store operand format");

                        let val = self.stack.pop().unwrap_or(Value::Nil);

                        // Using last_mut() gives us the last item as a MUTABLE reference
                        if let Some(current_scope) = self.scopes.last_mut() {
                            current_scope.insert(var_info.name, val);
                        }
                    }
                },

                "TemporaryStore" => {
                    // Check for value popped and index in operand
                    if let Some(val) = &self.stack.pop() {
                        if let Some(operand_json) = &instr.operand {
                            let slot = operand_json.as_u64().expect("Slot for a Temporary Store must be a positive integer");

                            // Now insert!
                            self.temporaries.insert(slot, val.clone());
                        }
                        else {
                            panic!("[IP {:04}] Operand was not given for temporarily storing.", ip);
                        }
                    }
                    else {
                        panic!("[IP {:04}] No value found to store in temporaries.", ip);
                    }
                }
                
                // Load a variable from the current scope or parent scopes
                // Scoping prevents scopes that were destroyed from being called
                "Load" => {
                    if let Some(operand_json) = &instr.operand {
                        let name = operand_json.as_str() // Because it is the name
                            .expect("The operand of a 'Load' must be a string name");

                        let mut found_val: Option<Value> = None; // Store a var for a value when it is found (or not...)

                        // Uh oh!!! Gotta reverse it to start from global
                        for scope in self.scopes.iter().rev() {
                            // Try and get the value
                            if let Some(val) = scope.get(name) {
                                found_val = Some(val.clone());
                                break; // We found it, done
                            }
                        }

                        if let Some(val) = found_val {
                            self.stack.push(val);
                        } else {
                            panic!("[IP {:04}] Variable '{}' is not defined in any active scope.", ip, name);
                        }
                    }
                }

                "TemporaryLoad" => {
                    // Check the operand
                    if let Some(operand_json) = &instr.operand {
                        let slot = operand_json.as_u64().expect("Slot for a Temporary Load must be a positive integer");

                        // Get object
                        let object = self.temporaries.get(&slot);

                        if let Some(obj) = object {
                            // Push as a CLONE
                            self.stack.push(obj.clone());
                        } else {
                            panic!("[IP {:04}] Attempted to load undefined temporary object of slot {}.", ip, slot)
                        }
                    }
                }

                // Search from innermost to outermost scope and update an existing variable
                "Update" => {
                    if let Some(operand_json) = &instr.operand {
                        // Get the update operand
                        let var_info: UpdateOperand = serde_json::from_value(operand_json.clone())
                            .expect("Invalid Update operand format");

                        // Also get the value
                        let new_val = self.stack.pop().unwrap_or(Value::Nil);
                        let mut updated = false;

                        // Through each scope, look for it
                        for scope in self.scopes.iter_mut().rev() {
                            if scope.contains_key(&var_info.name) {
                                scope.insert(var_info.name.clone(), new_val);

                                updated = true;
                                break;
                            }
                        }

                        // PANIC!!!
                        if !updated {
                            panic!("[IP {:04}] Cannot update undefined variable '{}'", ip, var_info.name);
                        }
                    }
                }

                "Index" => {
                    if let Some(index) = self.stack.pop() {
                        if let Some(target) = self.stack.pop() {
                            
                        }
                    }
                }

                "ForeachNext" => {
                    if let Some(operand_json) = &instr.operand {
                        let operand: ForeachOperand = serde_json::from_value(operand_json.clone())
                            .expect("Invalid ForeachNext operand format");

                        // Must be a list inside temporaries, yeah?
                        let list: Vec<Value> = match self.temporaries.get(&operand.collection_slot) {
                            Some(Value::List { items, element_type: _ }) => items.clone(),

                            Some(Value::Dictionary { key_type: _, value_type: _, allow_duplicates: _, keys, values }) => {
                                if keys.len() != values.len() {
                                    // When there is something odd (an error)
                                    panic!("There are {} keys but {} values in this dictionary being looped over.", keys.len(), values.len());
                                }

                                /*
                                    Iterates over the keys
                                    Combines them with values into a key-value like order using 'zip()'
                                    Maps them into a list of the keys and values
                                    Collects them to return (as a Vector of Values)
                                */
                                keys.iter()
                                    .zip(values.iter())
                                    .map(|(key, value)| Value::List {
                                            element_type: None,
                                            items: vec![key.clone(), value.clone()],
                                        })
                                    .collect()
                            }

                            _ => panic!("Can only iterate over an iterable collection"),
                        };

                        // Now get the index to assign with
                        let index = match self.temporaries.get(&operand.index_slot) {
                            Some(Value::Integer(i)) => *i as usize,
                            _ => panic!("Foreach index was not initialized"),
                        };

                        // When it's finished
                        if index >= list.len() {
                            self.stack.push(Value::Boolean(false));
                            // Move on to the next instruction
                        } else {
                            // We assign for a scope index
                            let mut var_scope_idx = None;

                            // Now we check for that variable name. If found, assign found scope to scope index
                            for (idx, scope) in self.scopes.iter().enumerate().rev() {
                                if scope.contains_key(&operand.variable_name) {
                                    var_scope_idx = Some(idx);
                                    break;
                                }
                            }

                            let scope_idx = var_scope_idx.unwrap_or_else(|| {
                                panic!("Variable was not defined yet: {}", operand.variable_name)
                            });

                            // Get the item from the list
                            let current_item = list[index].clone();

                            // We will eventually add type compatability
                            // if let Some(expected_type) = &operand.max_type {
                            //     Self::ensure_compatibility(expected_type, &current_item);
                            // }

                            // We now store the new item, and increment the index to get the next 'foreach' value
                            self.scopes[scope_idx].insert(operand.variable_name.clone(), current_item);
                            self.temporaries.insert(operand.index_slot.clone(), Value::Integer((index + 1) as i64));

                            // We also push Truth into the stack as a signal of success
                            self.stack.push(Value::Boolean(true));
                        }
                    }
                }

                // Legit just say something. Like what else
                "Say" => {
                    if let Some(val) = &self.stack.pop() {
                        println!("{}", val);
                    }
                }

                /*
                   -------------
                    ARITHMETICS
                   -------------
                */

                "Add" => {
                    // We are popping from the stack, so the right value comes first!
                    let right = self.stack.pop().unwrap_or(Value::Nil);
                    let left = self.stack.pop().unwrap_or(Value::Nil);

                    match (&left, &right) {
                        // Integer and Float adding
                        // With floats and integers, integers are promoted to floats
                        (Value::Integer(a), Value::Integer(b)) => {
                            self.stack.push(Value::Integer(a + b));
                        }
                        (Value::Float(a), Value::Float(b)) => {
                            self.stack.push(Value::Float(a + b));
                        }
                        (Value::Float(a), Value::Integer(b)) => {
                            self.stack.push(Value::Float(a + *b as f64));
                        }
                        (Value::Integer(a), Value::Float(b)) => {
                            self.stack.push(Value::Float(*a as f64 + b));
                        }

                        // 2. String Concatenation: If either side is a String, convert both using Display
                        (left_val, right_val) if matches!(left_val, Value::String(_)) || matches!(right_val, Value::String(_)) => {
                            let result = format!("{}{}", left_val, right_val);
                            self.stack.push(Value::String(result));
                        }

                        (left_val, right_val) => {
                            panic!(
                                "[IP {:04}] Cannot perform '+' operation between {:?} and {:?}",
                                ip, left_val, right_val
                            )
                        }
                    }
                }

                "Subtract" => {
                    // We are popping from the stack, so the right value comes first!
                    let right = self.stack.pop().unwrap_or(Value::Nil);
                    let left = self.stack.pop().unwrap_or(Value::Nil);

                    match (&left, &right) {
                        // Integer and Float adding
                        // With floats and integers, integers are promoted to floats
                        (Value::Integer(a), Value::Integer(b)) => {
                            self.stack.push(Value::Integer(a - b));
                        }
                        (Value::Float(a), Value::Float(b)) => {
                            self.stack.push(Value::Float(a - b));
                        }
                        (Value::Float(a), Value::Integer(b)) => {
                            self.stack.push(Value::Float(a - *b as f64));
                        }
                        (Value::Integer(a), Value::Float(b)) => {
                            self.stack.push(Value::Float(*a as f64 - b));
                        }

                        (left_val, right_val) => {
                            panic!(
                                "[IP {:04}] Cannot perform '-' operation between {:?} and {:?}",
                                ip, left_val, right_val
                            )
                        }
                    }
                }

                "Multiply" => {
                    // We are popping from the stack, so the right value comes first!
                    let right = self.stack.pop().unwrap_or(Value::Nil);
                    let left = self.stack.pop().unwrap_or(Value::Nil);

                    match (&left, &right) {
                        // Integer and Float adding
                        // With floats and integers, integers are promoted to floats
                        (Value::Integer(a), Value::Integer(b)) => {
                            self.stack.push(Value::Integer(a * b));
                        }
                        (Value::Float(a), Value::Float(b)) => {
                            self.stack.push(Value::Float(a * b));
                        }
                        (Value::Float(a), Value::Integer(b)) => {
                            self.stack.push(Value::Float(a * *b as f64));
                        }
                        (Value::Integer(a), Value::Float(b)) => {
                            self.stack.push(Value::Float(*a as f64 * b));
                        }

                        (left_val, right_val) => {
                            panic!(
                                "[IP {:04}] Cannot perform '*' operation between {:?} and {:?}",
                                ip, left_val, right_val
                            )
                        }
                    }
                }

                "Divide" => {
                    // We are popping from the stack, so the right value comes first!
                    let right = self.stack.pop().unwrap_or(Value::Nil);
                    let left = self.stack.pop().unwrap_or(Value::Nil);

                    match (&left, &right) {
                        // Integer and Float adding
                        // With floats and integers, integers are promoted to floats
                        (Value::Integer(a), Value::Integer(b)) => {
                            self.stack.push(Value::Integer(a / b));
                        }
                        (Value::Float(a), Value::Float(b)) => {
                            self.stack.push(Value::Float(a / b));
                        }
                        (Value::Float(a), Value::Integer(b)) => {
                            self.stack.push(Value::Float(a / *b as f64));
                        }
                        (Value::Integer(a), Value::Float(b)) => {
                            self.stack.push(Value::Float(*a as f64 / b));
                        }

                        (left_val, right_val) => {
                            panic!(
                                "[IP {:04}] Cannot perform '/' operation between {:?} and {:?}",
                                ip, left_val, right_val
                            )
                        }
                    }
                }

                "Compare" => {
                    let right = self.stack.pop().unwrap_or(Value::Nil);
                    let left = self.stack.pop().unwrap_or(Value::Nil);

                    if let Some(operator) = &instr.operand {
                        // Represent as  string, else just blank
                        let str_oper = operator.as_str().unwrap_or("");

                        let result = Self::eval_comparison(str_oper, left, right);

                        // Use evaluated value to push in the boolean(-like)!
                        self.stack.push(result);
                    }
                }

                /*
                 -----------------
                 ----- LOGIC -----
                 -----------------
                */

                "Not" => {
                    if let Some(val) = &instr.operand {
                        if let Some(booln) = val.as_bool() {
                            let new = Self::negate_boollike(&Value::Boolean(booln));

                            self.stack.push(new);
                        } else if let Some(pos) = val.as_str() {
                            if pos == "possible" {
                                let new = Self::negate_boollike(&Value::Possible);

                                self.stack.push(new);
                            }
                        }
                    }
                }

                /*
                 -------------------
                 ----- JUMPING -----
                 -------------------
                */

                "Jump" => {
                    if let Some(jump_to) = &instr.operand {
                        if let Some(jump_u) = jump_to.as_u64() {
                            ip = jump_u as usize;
                            continue; // We skip incremembting ip! This goes for the other jumps
                        }
                    }
                }

                // This checks for the condition before it to check if it can jump or not
                "JumpIfFalse" => {
                    if let Some(jump_to) = &instr.operand {
                        let condition = self.stack.pop().unwrap_or(Value::Nil);

                        // When false, DUH
                        if condition == Value::Boolean(false) {
                            if let Some(jumpif_u) = jump_to.as_u64() {
                                ip = jumpif_u as usize;
                                continue;
                            }
                        }
                    }
                }
                
                // Same here!
                "JumpIfPossible" => {
                    if let Some(jump_to) = &instr.operand {
                        let condition = self.stack.pop().unwrap_or(Value::Nil);

                        // When possible
                        if condition == Value::Possible {
                            if let Some(jumpif_u) = jump_to.as_u64() {
                                ip = jumpif_u as usize;
                                continue;
                            }
                        } else if matches!(condition, Value::Boolean(_)) {
                            // Push back ONLY valid booleans for JumpIfFalse to read!
                            // Kinda like drying the floor so the other guest doesn't slip
                            self.stack.push(condition);
                        }
                        else {
                            panic!("[IP {:04}] Statement inside condition must evaluate to a BoolLike, got {:?}", ip, condition);
                        }
                    }
                }

                /*
                 ------------------
                 --- CANCELLING ---
                 ------------------
                */

                "Throw" => {
                    if let Some(operand) = &instr.operand {
                        // Try and get the message attached to the C# Exception sent
                        if let Some(err_message) = operand.get("Message") {
                            panic!("{}", err_message);
                        }
                        else {
                            // Panicking when the point of this code is to panic. How ironic!
                            panic!("No error message attached to exception when throwing! Could not find traceback.")
                        }
                    }
                }

                unknown => eprintln!("[IP {:04}] Unhandled OpCode: {}", ip, unknown),
            }

            ip += 1; // To next instruction
        }

        println!("Execution complete");
    }

    /*
     ------------------------
     --- HELPER FUNCTIONS ---
     ------------------------
    */

    pub fn eval_comparison(operation: &str, left: Value, right: Value) -> Value {
        match operation {
            "==" => left.evaluate_equal(&right),
            "!=" => left.evaluate_not_equal(&right),
            
            ">" | ">=" | "<" | "<=" => left.evaluate_relational(operation, &right),

            _ => panic!("Unknown comparison operator: {}", operation),
        }
    }

    pub fn negate_boollike(bool: &Value) -> Value {
        match bool {
            Value::Boolean(val) => { Value::Boolean(!val) },
            Value::Possible => Value::Possible,

            _ => panic!("Can only negate bool like values, got {}", bool)
        }
    }

    pub fn evaluate_indexing(index: Value, target: Value) {
        match target {
            List { element_type, items } => {
                if index.compare_valuetypes(&Value::Integer(0)) {

                }
            }

            _ => panic!("Invalid target type for indexing")
        }
    }
}