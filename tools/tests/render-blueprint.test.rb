require "minitest/autorun"
require "yaml"

module StageBlueprintContract
  module_function

  def validate(document)
    errors = []
    services = document["services"]
    databases = document["databases"]

    unless services.is_a?(Array)
      return ["services must be a YAML sequence"]
    end
    unless databases.is_a?(Array)
      return ["databases must be a YAML sequence"]
    end

    web_services = services.select { |service| service.is_a?(Hash) && service["type"] == "web" }
    errors << "exactly one Stage web service is required" unless web_services.length == 1
    errors << "exactly one Stage database is required" unless databases.length == 1

    resources = services + databases
    resources.each do |resource|
      name = resource.is_a?(Hash) ? resource["name"].to_s : ""
      if name.match?(/\bproduction\b|(?:\A|[-_])prod(?:[-_]|\z)/i)
        errors << "Production-like resource names are forbidden: #{name}"
      end
    end

    web = web_services.first
    return errors unless web

    errors << "web service must be named mono-api-staging" unless web["name"] == "mono-api-staging"
    errors << "web service must use Docker" unless web["runtime"] == "docker"
    errors << "web service must use ./Dockerfile" unless web["dockerfilePath"] == "./Dockerfile"
    errors << "Stage autoDeploy must remain disabled" unless web["autoDeploy"] == false
    errors << "web service must use /health/ready" unless web["healthCheckPath"] == "/health/ready"

    branch = web["branch"].to_s
    unless branch.match?(/\Asprint-[a-z0-9-]+\z/) && branch != "sprint-main"
      errors << "web service must track a Sprint candidate branch"
    end

    env_vars = web["envVars"]
    env_vars = [] unless env_vars.is_a?(Array)
    env = env_vars.each_with_object({}) do |entry, result|
      result[entry["key"]] = entry if entry.is_a?(Hash)
    end

    errors << "ASPNETCORE_ENVIRONMENT must be Staging" unless env.dig("ASPNETCORE_ENVIRONMENT", "value") == "Staging"
    errors << "MONO_AUTO_MIGRATE must be true for the Stage database" unless env.dig("MONO_AUTO_MIGRATE", "value") == "true"

    connection = env["ConnectionStrings__Mono"]
    db_reference = connection.is_a?(Hash) ? connection["fromDatabase"] : nil
    unless db_reference.is_a?(Hash) && db_reference["name"] == "mono-stage-db" && db_reference["property"] == "connectionString"
      errors << "API connection string must reference mono-stage-db"
    end

    database = databases.find { |entry| entry.is_a?(Hash) && entry["name"] == "mono-stage-db" }
    errors << "database must be named mono-stage-db" unless database
    if database
      errors << "databaseName must be mono_stage" unless database["databaseName"] == "mono_stage"
      errors << "database user must be mono_stage" unless database["user"] == "mono_stage"
      errors << "Stage database must not allow external IP access" unless database["ipAllowList"] == []
    end

    errors
  end
end

class StageBlueprintContractTest < Minitest::Test
  def setup
    @document = YAML.safe_load(File.read(File.expand_path("../../render.yaml", __dir__)), aliases: false)
  end

  def test_current_blueprint_matches_stage_only_contract
    assert_empty StageBlueprintContract.validate(@document)
  end

  def test_rejects_enabled_auto_deploy
    document = copy_document
    document["services"].first["autoDeploy"] = true
    assert_includes StageBlueprintContract.validate(document), "Stage autoDeploy must remain disabled"
  end

  def test_rejects_production_environment
    document = copy_document
    document["services"].first["envVars"].find { |entry| entry["key"] == "ASPNETCORE_ENVIRONMENT" }["value"] = "Production"
    assert_includes StageBlueprintContract.validate(document), "ASPNETCORE_ENVIRONMENT must be Staging"
  end

  def test_rejects_production_database_reference
    document = copy_document
    document["services"].first["envVars"].find { |entry| entry["key"] == "ConnectionStrings__Mono" }["fromDatabase"]["name"] = "mono-production-db"
    assert StageBlueprintContract.validate(document).any? { |error| error.include?("Production-like resource names") || error == "API connection string must reference mono-stage-db" }
  end

  def test_rejects_non_candidate_branch
    document = copy_document
    document["services"].first["branch"] = "main"
    assert_includes StageBlueprintContract.validate(document), "web service must track a Sprint candidate branch"
  end

  def test_rejects_public_database_allowlist
    document = copy_document
    document["databases"].first["ipAllowList"] = [{ "ip" => "0.0.0.0/0" }]
    assert_includes StageBlueprintContract.validate(document), "Stage database must not allow external IP access"
  end

  def test_rejects_wrong_readiness_endpoint
    document = copy_document
    document["services"].first["healthCheckPath"] = "/health/live"
    assert_includes StageBlueprintContract.validate(document), "web service must use /health/ready"
  end

  private

  def copy_document
    Marshal.load(Marshal.dump(@document))
  end
end
