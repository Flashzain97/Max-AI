use serde::Deserialize;


// ---------------- \\
// --- OPERANDS --- \\
// ---------------- \\

/// The operand inside a Store instruction
#[derive(Debug, Deserialize)]
#[serde(rename_all = "PascalCase")]
pub struct StoreOperand {
    pub name: String,
    #[serde(default)]
    pub r#type: Option<String>,
    #[serde(default)]
    pub resort_value: Option<serde_json::Value>
}

/// The operand inside an Update instruction
#[derive(Debug, Deserialize)]
#[serde(rename_all = "PascalCase")]
pub struct UpdateOperand {
    #[serde(default)]
    pub name: String,
    #[serde(default)]
    pub operation: String
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "PascalCase")]
pub struct ForeachOperand {
    pub collection_slot: u64,
    pub index_slot: u64,
    pub variable_name: String,
    #[serde(default)]
    pub max_type: Option<String>
}

// ------------ \\
// --- MAIN --- \\
// ------------ \\

/// A single instruction emitted by the C# compiler
#[derive(Debug, Deserialize)]
#[serde(rename_all = "PascalCase")]
pub struct Instruction {
    pub op_code: String,
    // Using serde_json::Value allows 'operand' to be a String, Object, or null dynamically
    pub operand: Option<serde_json::Value>
}

/// The Bytecode object that contains all instructions, tasks, etc..., for the VM to read
#[derive(Debug, Deserialize)]
#[serde(rename_all = "PascalCase")]
pub struct Bytecode {
    pub instructions: Vec<Instruction>,
    pub tasks: serde_json::Value,
    pub coroutines: serde_json::Value,
}