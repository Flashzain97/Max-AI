// cargo run -- ../bytecode_json/program.json

// ----- IMPORT CREATED CRATES -----\\

pub mod bytecode;
use crate::bytecode::*;

pub mod vm;
use crate::vm::VirtualMachine;

// ------ \\

use std::{env, fs};
use std::path::Path;

// ------ \\

fn main() {
    // Get arguments when run
    let args: Vec<String> = env::args().collect();
    let path_str = args.get(1).expect("Please provide a path to program.json");
    
    // Argument must be the pathing to the json file
    let path = Path::new(path_str);
    let full_path = env::current_dir().unwrap().join(path);

    // Read the json
    let json_data = fs::read_to_string(path)
        .unwrap_or_else(|err| panic!("Failed to read bytecode at {:?}: {}", full_path, err));

    // Extract bytecode from json
    let bytecode: Bytecode = serde_json::from_str(&json_data)
        .expect("Failed to parse bytecode JSON");

    // VM executes bytecode
    let mut vm = VirtualMachine::new();
    vm.run(bytecode);
}