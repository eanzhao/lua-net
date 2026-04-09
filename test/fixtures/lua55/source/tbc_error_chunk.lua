log = ""

local mt = {
  __close = function(self, err)
    local part = self.tag
    if err then
      part = part .. ":" .. err
    end

    log = log .. "[" .. part .. "]"

    if self.fail then
      error(self.message)
    end
  end
}

local function run()
  local a <close> = setmetatable({ tag = "a" }, mt)
  local b <close> = setmetatable({ tag = "b", fail = true, message = "close-b" }, mt)
  error("boom")
end

run()
